using System.Text.Json;
using MyClaw.Core.VectorMemory;

namespace MyClaw.Core.Tests.VectorMemory;

/// <summary>
/// 嵌入算法升级时的向量库自动重嵌入迁移测试。
/// </summary>
public class EmbeddingMigrationTests : IDisposable
{
    private readonly string _workspace;

    public EmbeddingMigrationTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), $"embed_migration_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_workspace, "memory"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, recursive: true);
        }
    }

    [Fact]
    public void PersistentStore_LegacyFileWithoutEmbeddingVersion_LoadsAsVersionZero()
    {
        // 旧文件未记录 EmbeddingVersion，反序列化应得到 0（视为遗留版本）
        var json = JsonSerializer.Serialize(new VectorStoreData
        {
            Version = 1,
            Dimension = 128,
            SavedAt = DateTime.UtcNow,
            Entries = new List<VectorMemoryEntry>()
        });
        var data = JsonSerializer.Deserialize<VectorStoreData>(json);

        Assert.NotNull(data);
        Assert.Equal(0, data!.EmbeddingVersion);
    }

    [Fact]
    public async Task InMemoryStore_LegacyArrayFormat_LoadsAsVersionZero()
    {
        // 旧格式为纯条目数组，加载后 LoadedEmbeddingVersion 应为 0
        var path = Path.Combine(_workspace, "memory", "legacy.json");
        var legacy = new List<VectorMemoryEntry>
        {
            new() { Id = "a", Content = "hello world", Embedding = new float[128] }
        };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(legacy));

        var store = new InMemoryVectorStore(128);
        await store.LoadAsync(path);

        Assert.Equal(0, store.LoadedEmbeddingVersion);
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public async Task InMemoryStore_RoundTripsEmbeddingVersion()
    {
        var path = Path.Combine(_workspace, "memory", "roundtrip.json");
        var store = new InMemoryVectorStore(128) { EmbeddingVersion = 2 };
        await store.UpsertAsync(new VectorMemoryEntry { Id = "x", Content = "abc", Embedding = new float[128] });
        await store.SaveAsync(path);

        var reloaded = new InMemoryVectorStore(128);
        await reloaded.LoadAsync(path);

        Assert.Equal(2, reloaded.LoadedEmbeddingVersion);
    }

    [Fact]
    public async Task Manager_LegacyVectors_AreReEmbeddedOnInitialize()
    {
        // 写入遗留 vectors.json（纯数组、stale 全零嵌入），管理器初始化时应自动重嵌入
        const int dim = 128;
        var path = Path.Combine(_workspace, "memory", "vectors.json");
        var legacy = new List<VectorMemoryEntry>
        {
            new() { Id = "m1", Content = "machine learning models embed text", Embedding = new float[dim], SourceType = "long_term" },
            new() { Id = "m2", Content = "数字生命体拥有灵魂与记忆", Embedding = new float[dim], SourceType = "daily_log" }
        };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(legacy));

        var manager = new VectorMemoryManager(_workspace, dimension: dim, persistent: false);
        await manager.InitializeAsync();

        // 期望：每个条目的嵌入已被当前 FNV 嵌入服务重算（非全零，且与 EmbedAsync(Content) 一致）
        var expected1 = await manager.EmbeddingService.EmbedAsync(legacy[0].Content);
        var migrated1 = await manager.VectorStore.GetAsync("m1");

        Assert.NotNull(migrated1);
        Assert.Contains(migrated1!.Embedding, v => v != 0f);
        Assert.Equal(expected1, migrated1.Embedding);

        // 迁移后活动版本应为当前版本，写盘文件记录最新版本
        Assert.Equal(manager.EmbeddingService.EmbeddingVersion, manager.VectorStore.EmbeddingVersion);

        var reloaded = new InMemoryVectorStore(dim);
        await reloaded.LoadAsync(path);
        Assert.Equal(manager.EmbeddingService.EmbeddingVersion, reloaded.LoadedEmbeddingVersion);
    }

    [Fact]
    public async Task Manager_EmptyStore_DoesNotMigrateButRecordsVersion()
    {
        var manager = new VectorMemoryManager(_workspace, dimension: 128, persistent: false);
        await manager.InitializeAsync();

        Assert.Equal(0, manager.VectorStore.Count);
        Assert.Equal(manager.EmbeddingService.EmbeddingVersion, manager.VectorStore.EmbeddingVersion);
    }
}
