namespace MyClaw.Core.Diagnostics;

/// <summary>
/// 单项诊断的结果状态。
/// </summary>
public enum DiagnosticStatus
{
    /// <summary>检查通过。</summary>
    Pass,

    /// <summary>非致命问题，功能可能受限。</summary>
    Warn,

    /// <summary>致命问题，核心功能不可用。</summary>
    Fail
}

/// <summary>
/// 单项诊断检查的结果。
/// </summary>
public class DiagnosticCheck
{
    /// <summary>检查项名称。</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>检查状态。</summary>
    public DiagnosticStatus Status { get; init; }

    /// <summary>对当前状态的简要说明。</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>修复建议（仅在 Warn/Fail 时有意义）。</summary>
    public string? Hint { get; init; }
}

/// <summary>
/// 一次自诊断的汇总报告。
/// </summary>
public class DiagnosticReport
{
    /// <summary>所有检查项（按执行顺序）。</summary>
    public List<DiagnosticCheck> Checks { get; } = new();

    /// <summary>是否存在致命问题。</summary>
    public bool HasFailures => Checks.Any(c => c.Status == DiagnosticStatus.Fail);

    /// <summary>是否存在告警。</summary>
    public bool HasWarnings => Checks.Any(c => c.Status == DiagnosticStatus.Warn);

    /// <summary>通过项数量。</summary>
    public int PassCount => Checks.Count(c => c.Status == DiagnosticStatus.Pass);

    /// <summary>告警项数量。</summary>
    public int WarnCount => Checks.Count(c => c.Status == DiagnosticStatus.Warn);

    /// <summary>致命项数量。</summary>
    public int FailCount => Checks.Count(c => c.Status == DiagnosticStatus.Fail);
}
