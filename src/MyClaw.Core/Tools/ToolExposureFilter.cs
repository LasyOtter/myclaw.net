namespace MyClaw.Core.Tools;

/// <summary>
/// 工具暴露过滤器（对标 hermes-agent 的 allow/deny 工具暴露控制）。
///
/// 规则（按优先级）：
/// <list type="number">
///   <item>命中拒绝列表 → 拒绝；</item>
///   <item>允许列表非空且未命中 → 拒绝；</item>
///   <item>否则 → 允许。</item>
/// </list>
/// 两个列表均为空时不做任何限制（暴露全部工具，与历史行为一致）。
/// 名称比较大小写不敏感。
/// </summary>
public class ToolExposureFilter
{
    private readonly HashSet<string> _allowed;
    private readonly HashSet<string> _blocked;

    public ToolExposureFilter(IEnumerable<string>? allowedTools, IEnumerable<string>? blockedTools)
    {
        _allowed = new HashSet<string>(
            allowedTools ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        _blocked = new HashSet<string>(
            blockedTools ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 是否未配置任何过滤规则（即暴露全部工具）。
    /// </summary>
    public bool IsUnrestricted => _allowed.Count == 0 && _blocked.Count == 0;

    /// <summary>
    /// 判断某个工具是否应被暴露/允许调用。
    /// </summary>
    public bool IsAllowed(string toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName)) return false;
        if (_blocked.Contains(toolName)) return false;
        if (_allowed.Count > 0 && !_allowed.Contains(toolName)) return false;
        return true;
    }

    /// <summary>
    /// 过滤一组工具名，仅保留被允许的。
    /// </summary>
    public IEnumerable<string> Filter(IEnumerable<string> toolNames)
        => toolNames.Where(IsAllowed);
}
