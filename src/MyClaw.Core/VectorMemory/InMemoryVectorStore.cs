using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MyClaw.Core.VectorMemory;

/// <summary>
/// 内存向量存储 - 无需外部依赖的向量数据库实现
/// 使用余弦相似度进行搜索
/// </summary>
public class InMemoryVectorStore : IVectorStore
{
    private readonly Dictionary<string, VectorMemoryEntry> _entries = new();
    private readonly int _dimension;
    private readonly object _lock = new();

    public int Dimension => _dimension;
    public int Count => _entries.Count;

    /// <summary>
    /// 已加载文件记录的嵌入算法版本（无文件/旧格式未记录时为 0）。
    /// </summary>
    public int LoadedEmbeddingVersion { get; private set; }

    /// <summary>
    /// 写盘时记录的嵌入算法版本。
    /// </summary>
    public int EmbeddingVersion { get; set; }

    public InMemoryVectorStore(int dimension = 384)
    {
        _dimension = dimension;
    }

    public Task<string> UpsertAsync(VectorMemoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        lock (_lock)
        {
            if (string.IsNullOrEmpty(entry.Id))
            {
                entry.Id = Guid.NewGuid().ToString();
            }

            entry.LastAccessedAt = DateTime.UtcNow;
            _entries[entry.Id] = entry;
        }

        return Task.FromResult(entry.Id);
    }

    public Task<int> UpsertBatchAsync(IEnumerable<VectorMemoryEntry> entries)
    {
        var count = 0;
        lock (_lock)
        {
            foreach (var entry in entries)
            {
                if (string.IsNullOrEmpty(entry.Id))
                {
                    entry.Id = Guid.NewGuid().ToString();
                }
                entry.LastAccessedAt = DateTime.UtcNow;
                _entries[entry.Id] = entry;
                count++;
            }
        }
        return Task.FromResult(count);
    }

    public Task<VectorMemoryEntry?> GetAsync(string id)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(id, out var entry))
            {
                entry.LastAccessedAt = DateTime.UtcNow;
                entry.AccessCount++;
                return Task.FromResult<VectorMemoryEntry?>(entry);
            }
            return Task.FromResult<VectorMemoryEntry?>(null);
        }
    }

    public Task<bool> DeleteAsync(string id)
    {
        lock (_lock)
        {
            return Task.FromResult(_entries.Remove(id));
        }
    }

    public Task ClearAsync()
    {
        lock (_lock)
        {
            _entries.Clear();
        }
        return Task.CompletedTask;
    }

    public Task<List<VectorSearchResult>> SearchAsync(VectorSearchRequest request)
    {
        var sw = Stopwatch.StartNew();
        var results = new List<VectorSearchResult>();

        float[] queryVector;
        lock (_lock)
        {
            if (request.QueryVector != null)
            {
                queryVector = request.QueryVector;
            }
            else
            {
                // 如果没有提供向量，需要外部嵌入服务
                // 这里返回空结果，实际使用时应先嵌入查询
                return Task.FromResult(results);
            }

            foreach (var entry in _entries.Values)
            {
                // 过滤来源类型
                if (request.SourceTypes != null && request.SourceTypes.Count > 0)
                {
                    if (!request.SourceTypes.Contains(entry.SourceType))
                        continue;
                }

                // 过滤元数据
                if (request.MetadataFilter != null)
                {
                    var match = true;
                    foreach (var filter in request.MetadataFilter)
                    {
                        if (!entry.Metadata.TryGetValue(filter.Key, out var value) || value != filter.Value)
                        {
                            match = false;
                            break;
                        }
                    }
                    if (!match) continue;
                }

                // 计算相似度
                var score = CosineSimilarity(queryVector, entry.Embedding);

                if (score >= request.MinScore)
                {
                    results.Add(new VectorSearchResult
                    {
                        Entry = entry,
                        Score = score
                    });
                }
            }
        }

        // 排序并取 TopK
        var sortedResults = results
            .OrderByDescending(r => r.Score)
            .Take(request.TopK)
            .ToList();

        sw.Stop();

        foreach (var result in sortedResults)
        {
            result.ElapsedMs = sw.ElapsedMilliseconds;
        }

        return Task.FromResult(sortedResults);
    }

    /// <summary>
    /// 使用预计算的查询向量进行搜索
    /// </summary>
    public List<VectorSearchResult> SearchWithVector(float[] queryVector, int topK = 5, double minScore = 0.0)
    {
        var results = new List<VectorSearchResult>();

        lock (_lock)
        {
            foreach (var entry in _entries.Values)
            {
                var score = CosineSimilarity(queryVector, entry.Embedding);
                if (score >= minScore)
                {
                    results.Add(new VectorSearchResult
                    {
                        Entry = entry,
                        Score = score
                    });
                }
            }
        }

        return results.OrderByDescending(r => r.Score).Take(topK).ToList();
    }

    public async Task SaveAsync(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        List<VectorMemoryEntry> entriesCopy;
        lock (_lock)
        {
            entriesCopy = _entries.Values.ToList();
        }

        var data = new VectorStoreData
        {
            Version = 2,
            EmbeddingVersion = EmbeddingVersion,
            Dimension = _dimension,
            SavedAt = DateTime.UtcNow,
            Entries = entriesCopy
        };

        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        await File.WriteAllTextAsync(path, json);
    }

    public async Task LoadAsync(string path)
    {
        if (!File.Exists(path)) return;

        var json = await File.ReadAllTextAsync(path);

        // 兼容两种格式：新版 VectorStoreData 包装对象，或旧版纯条目数组。
        List<VectorMemoryEntry>? entries;
        int loadedEmbeddingVersion = 0;
        var trimmed = json.TrimStart();
        if (trimmed.StartsWith('['))
        {
            // 旧格式：纯 List<VectorMemoryEntry>，未记录嵌入版本（视为 0）
            entries = JsonSerializer.Deserialize<List<VectorMemoryEntry>>(json);
        }
        else
        {
            var data = JsonSerializer.Deserialize<VectorStoreData>(json);
            entries = data?.Entries;
            loadedEmbeddingVersion = data?.EmbeddingVersion ?? 0;
        }

        if (entries != null)
        {
            lock (_lock)
            {
                _entries.Clear();
                foreach (var entry in entries)
                {
                    if (!string.IsNullOrEmpty(entry.Id))
                    {
                        _entries[entry.Id] = entry;
                    }
                }
            }
        }

        LoadedEmbeddingVersion = loadedEmbeddingVersion;
    }

    /// <summary>
    /// 计算余弦相似度
    /// </summary>
    private static double CosineSimilarity(float[] vector1, float[] vector2)
    {
        if (vector1.Length != vector2.Length || vector1.Length == 0)
            return 0;

        double dotProduct = 0;
        double magnitude1 = 0;
        double magnitude2 = 0;

        for (int i = 0; i < vector1.Length; i++)
        {
            dotProduct += vector1[i] * vector2[i];
            magnitude1 += vector1[i] * vector1[i];
            magnitude2 += vector2[i] * vector2[i];
        }

        magnitude1 = Math.Sqrt(magnitude1);
        magnitude2 = Math.Sqrt(magnitude2);

        if (magnitude1 == 0 || magnitude2 == 0)
            return 0;

        return dotProduct / (magnitude1 * magnitude2);
    }

    /// <summary>
    /// 获取所有条目 (用于调试)
    /// </summary>
    public IEnumerable<VectorMemoryEntry> GetAllEntries()
    {
        lock (_lock)
        {
            return _entries.Values.ToList();
        }
    }
}
