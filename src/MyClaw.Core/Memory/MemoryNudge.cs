namespace MyClaw.Core.Memory;

/// <summary>
/// 记忆 nudge 的类别（对标 hermes-agent 的记忆提醒/自我提示机制）。
/// </summary>
public enum MemoryNudgeKind
{
    /// <summary>长期记忆 MEMORY.md 为空。</summary>
    EmptyLongTerm,

    /// <summary>今天尚未记录任何记忆。</summary>
    NoMemoryToday,

    /// <summary>今日记忆条目过多，建议蒸馏/归档。</summary>
    NeedsDistillation,

    /// <summary>近期记忆里存在未解决的问题/待办。</summary>
    OpenQuestions
}

/// <summary>
/// 单条记忆 nudge：一个类别 + 一句可读的提醒。
/// </summary>
public class MemoryNudge
{
    public MemoryNudgeKind Kind { get; init; }
    public string Message { get; init; } = string.Empty;
}

/// <summary>
/// 一次 nudge 评估的结果。
/// </summary>
public class MemoryNudgeResult
{
    public List<MemoryNudge> Nudges { get; } = new();

    public bool HasNudges => Nudges.Count > 0;
}
