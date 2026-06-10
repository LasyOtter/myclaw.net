using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyClaw.Core.Serialization;

/// <summary>
/// 共享的 <see cref="JsonSerializerOptions"/> 单例。
///
/// 原先全仓 20+ 处在每次 (反)序列化时都 <c>new JsonSerializerOptions{...}</c>，
/// 既增加分配，又因 options 首次使用后会被冻结而无法复用。这里按用途集中缓存为
/// <c>static readonly</c> 单例；它们仅用于 (反)序列化、创建后不再修改，复用是安全的。
/// </summary>
public static class JsonOptions
{
    /// <summary>反序列化：属性名大小写不敏感。</summary>
    public static readonly JsonSerializerOptions CaseInsensitiveRead = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>序列化：缩进（人类可读的持久化文件）。</summary>
    public static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true
    };

    /// <summary>序列化：紧凑（不缩进，体积优先）。</summary>
    public static readonly JsonSerializerOptions Compact = new()
    {
        WriteIndented = false
    };

    /// <summary>序列化：缩进 + camelCase（RIBOSOME 等需要 camelCase 的文件）。</summary>
    public static readonly JsonSerializerOptions CamelCaseIndented = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>反序列化：大小写不敏感 + camelCase 命名策略。</summary>
    public static readonly JsonSerializerOptions CamelCaseCaseInsensitiveRead = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>反序列化配置文件：大小写不敏感 + 允许注释。</summary>
    public static readonly JsonSerializerOptions ConfigRead = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>序列化配置文件：缩进 + camelCase + 忽略 null。</summary>
    public static readonly JsonSerializerOptions ConfigWrite = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
