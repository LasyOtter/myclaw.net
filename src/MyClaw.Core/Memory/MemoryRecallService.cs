using System.Text;
using MyClaw.Core.VectorMemory;

namespace MyClaw.Core.Memory;

/// <summary>
/// 单条召回命中
/// </summary>
public class MemoryRecallHit
{
    /// <summary>
    /// 来源描述，例如 "2026-05-01.md (归档)"
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// 命中片段
    /// </summary>
    public string Snippet { get; set; } = string.Empty;

    /// <summary>
    /// 相关度分数 (0-1)；关键词回退时为匹配关键词的归一化占比
    /// </summary>
    public double Score { get; set; }
}

/// <summary>
/// 跨会话记忆召回结果
/// </summary>
public class MemoryRecallResult
{
    public string Query { get; set; } = string.Empty;

    public List<MemoryRecallHit> Hits { get; set; } = new();

    /// <summary>
    /// true 表示使用语义检索命中；false 表示回退到关键词匹配（或无命中）
    /// </summary>
    public bool UsedSemantic { get; set; }

    /// <summary>
    /// 参与检索的记忆文件数量
    /// </summary>
    public int DocumentsScanned { get; set; }
}

/// <summary>
/// 跨会话记忆召回服务。
///
/// myclaw 原有的 <c>myclaw_read</c> 只能读取"今日日志 + MEMORY.md"，而 <c>memory/</c>
/// 下的每日历史与 <c>memory/archived/</c> 归档日志无法被检索。本服务参照 hermes-agent 的
/// 跨会话检索能力，统一对以下来源做检索：
/// <list type="bullet">
///   <item>MEMORY.md（长期记忆）</item>
///   <item>memory/*.md（每日日志，含今日）</item>
///   <item>memory/archived/*.md（归档日志）</item>
/// </list>
/// 优先用现有的向量记忆做语义排序；当语义检索无命中（或索引为空）时回退到关键词匹配，
/// 保证短查询/无嵌入场景下也有可用结果。
/// </summary>
public class MemoryRecallService
{
    private readonly string _workspace;
    private readonly string _memoryDir;
    private readonly string _archivedDir;
    private readonly int _dimension;

    public MemoryRecallService(string workspace, int dimension = 384)
    {
        _workspace = workspace;
        _memoryDir = Path.Combine(workspace, "memory");
        _archivedDir = Path.Combine(_memoryDir, "archived");
        _dimension = dimension;
    }

    /// <summary>
    /// 跨会话召回与查询最相关的记忆。
    /// </summary>
    /// <param name="query">自然语言查询</param>
    /// <param name="topK">返回条数上限</param>
    /// <param name="minScore">语义检索的最小相关度阈值</param>
    public async Task<MemoryRecallResult> RecallAsync(string query, int topK = 5, double minScore = 0.2)
    {
        var result = new MemoryRecallResult { Query = query ?? string.Empty };

        if (string.IsNullOrWhiteSpace(query))
        {
            return result;
        }

        if (topK <= 0)
        {
            topK = 5;
        }

        var documents = CollectDocuments();
        result.DocumentsScanned = documents.Count;
        if (documents.Count == 0)
        {
            return result;
        }

        // 1. 语义检索：用一个临时的内存索引（避免触碰持久化向量库）
        var store = new InMemoryVectorStore(_dimension);
        var embedding = new SimpleEmbeddingService(_dimension);
        var retriever = new RagRetriever(store, embedding);

        foreach (var doc in documents)
        {
            await retriever.IndexAsync(doc.Content, doc.SourceType, doc.Path, importance: doc.Importance);
        }

        var rag = await retriever.SearchAsync(query, topK, minScore);
        if (rag.Sources.Count > 0)
        {
            result.UsedSemantic = true;
            foreach (var source in rag.Sources)
            {
                result.Hits.Add(new MemoryRecallHit
                {
                    Source = DescribeSource(source.Entry.SourcePath, source.Entry.SourceType),
                    Snippet = Truncate(source.Entry.Content.Trim(), 400),
                    Score = source.Score
                });
            }
            return result;
        }

        // 2. 关键词回退
        result.Hits.AddRange(KeywordFallback(query, documents, topK));
        return result;
    }

    private List<MemoryDocument> CollectDocuments()
    {
        var docs = new List<MemoryDocument>();

        var longTermPath = Path.Combine(_workspace, "MEMORY.md");
        AddIfPresent(docs, longTermPath, "long_term", importance: 0.9);

        if (Directory.Exists(_memoryDir))
        {
            // 每日日志（含今日）；归档放在子目录，单独处理，避免重复
            foreach (var file in Directory.EnumerateFiles(_memoryDir, "*.md", SearchOption.TopDirectoryOnly))
            {
                AddIfPresent(docs, file, "daily_log", importance: 0.7);
            }
        }

        if (Directory.Exists(_archivedDir))
        {
            foreach (var file in Directory.EnumerateFiles(_archivedDir, "*.md", SearchOption.TopDirectoryOnly))
            {
                AddIfPresent(docs, file, "archived_log", importance: 0.6);
            }
        }

        return docs;
    }

    private static void AddIfPresent(List<MemoryDocument> docs, string path, string sourceType, double importance)
    {
        if (!File.Exists(path))
        {
            return;
        }

        string content;
        try
        {
            content = File.ReadAllText(path);
        }
        catch
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        docs.Add(new MemoryDocument(path, sourceType, content, importance));
    }

    private IEnumerable<MemoryRecallHit> KeywordFallback(string query, List<MemoryDocument> documents, int topK)
    {
        var keywords = ExtractKeywords(query);
        if (keywords.Count == 0)
        {
            return Enumerable.Empty<MemoryRecallHit>();
        }

        var hits = new List<MemoryRecallHit>();

        foreach (var doc in documents)
        {
            var lines = doc.Content.Split('\n');
            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                var lower = line.ToLowerInvariant();
                var matched = keywords.Count(k => lower.Contains(k));
                if (matched == 0)
                {
                    continue;
                }

                hits.Add(new MemoryRecallHit
                {
                    Source = DescribeSource(doc.Path, doc.SourceType),
                    Snippet = Truncate(line, 400),
                    Score = (double)matched / keywords.Count
                });
            }
        }

        return hits
            .OrderByDescending(h => h.Score)
            .ThenByDescending(h => h.Snippet.Length)
            .Take(topK)
            .ToList();
    }

    private static List<string> ExtractKeywords(string query)
    {
        return query
            .ToLowerInvariant()
            .Split(new[] { ' ', '\t', '\n', '\r', '.', ',', '!', '?', ';', ':', '(', ')', '[', ']', '"', '\'' },
                StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 2)
            .Distinct()
            .ToList();
    }

    private string DescribeSource(string? sourcePath, string sourceType)
    {
        var label = sourceType switch
        {
            "long_term" => "长期记忆",
            "daily_log" => "每日日志",
            "archived_log" => "归档",
            _ => sourceType
        };

        if (string.IsNullOrEmpty(sourcePath))
        {
            return label;
        }

        return $"{Path.GetFileName(sourcePath)} ({label})";
    }

    private static string Truncate(string text, int maxLength)
    {
        if (text.Length <= maxLength)
        {
            return text;
        }

        return text.Substring(0, maxLength) + " …";
    }

    private readonly record struct MemoryDocument(string Path, string SourceType, string Content, double Importance);
}
