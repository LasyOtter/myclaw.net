namespace MyClaw.Core.VectorMemory;

/// <summary>
/// 向量存储接口
/// </summary>
public interface IVectorStore
{
    /// <summary>
    /// 向量维度
    /// </summary>
    int Dimension { get; }

    /// <summary>
    /// 存储条目数量
    /// </summary>
    int Count { get; }

    /// <summary>
    /// 添加或更新条目
    /// </summary>
    Task<string> UpsertAsync(VectorMemoryEntry entry);

    /// <summary>
    /// 批量添加条目
    /// </summary>
    Task<int> UpsertBatchAsync(IEnumerable<VectorMemoryEntry> entries);

    /// <summary>
    /// 获取条目
    /// </summary>
    Task<VectorMemoryEntry?> GetAsync(string id);

    /// <summary>
    /// 删除条目
    /// </summary>
    Task<bool> DeleteAsync(string id);

    /// <summary>
    /// 清空所有条目
    /// </summary>
    Task ClearAsync();

    /// <summary>
    /// 向量相似度搜索
    /// </summary>
    Task<List<VectorSearchResult>> SearchAsync(VectorSearchRequest request);

    /// <summary>
    /// 持久化到文件
    /// </summary>
    Task SaveAsync(string path);

    /// <summary>
    /// 从文件加载
    /// </summary>
    Task LoadAsync(string path);

    /// <summary>
    /// 已加载文件中记录的嵌入算法版本（无文件/旧格式未记录时为 0）。
    /// 用于启动时判定是否需要重嵌入迁移。
    /// </summary>
    int LoadedEmbeddingVersion { get; }

    /// <summary>
    /// 写盘时记录的嵌入算法版本，由管理器设置为当前嵌入服务的版本。
    /// </summary>
    int EmbeddingVersion { get; set; }

    /// <summary>
    /// 枚举全部条目（用于迁移/调试）。
    /// </summary>
    IEnumerable<VectorMemoryEntry> GetAllEntries();
}
