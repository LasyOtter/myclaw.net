namespace MyClaw.Core.Memory;

/// <summary>
/// 记忆 nudge 服务（对标 hermes-agent 的记忆提醒/自我提示）。
///
/// 不同于 <see cref="MemoryRecallService"/>（显式查询 → 召回相关记忆），nudge 是
/// <b>无需提问</b>、基于当前记忆状态的启发式提醒：长期记忆是否为空、今天是否还没记录、
/// 当日日志是否该蒸馏、近期是否有未解决的问题。用于在 boot/heartbeat 时轻推宿主主动维护记忆。
///
/// 仅依赖 <see cref="MemoryStore"/> 的公开读取方法，纯逻辑、可单测。
/// </summary>
public class MemoryNudgeService
{
    private readonly MemoryStore _memoryStore;

    /// <summary>当日记忆条目达到该阈值时，提示蒸馏/归档。</summary>
    public const int DistillThreshold = 20;

    /// <summary>nudge 中最多列出的未解决问题数量。</summary>
    public const int MaxOpenQuestions = 3;

    private static readonly string[] OpenQuestionMarkers =
        { "TODO", "todo", "FIXME", "待办", "未完成", "未解决", "问题", "?", "？" };

    public MemoryNudgeService(MemoryStore memoryStore)
    {
        _memoryStore = memoryStore;
    }

    /// <summary>
    /// 评估并生成记忆 nudge。
    /// </summary>
    /// <param name="recentDays">扫描未解决问题时回溯的天数（含今日）。</param>
    public MemoryNudgeResult Generate(int recentDays = 3)
    {
        if (recentDays < 1) recentDays = 1;
        var result = new MemoryNudgeResult();

        // 1. 长期记忆为空
        if (string.IsNullOrWhiteSpace(_memoryStore.ReadLongTerm()))
        {
            result.Nudges.Add(new MemoryNudge
            {
                Kind = MemoryNudgeKind.EmptyLongTerm,
                Message = "长期记忆 MEMORY.md 为空，建议用 myclaw_update 把稳定事实/项目背景沉淀进去。"
            });
        }

        // 2. 今日是否已有记录；条目过多则提示蒸馏
        var todayEntryCount = CountEntries(_memoryStore.ReadToday());
        if (todayEntryCount == 0)
        {
            result.Nudges.Add(new MemoryNudge
            {
                Kind = MemoryNudgeKind.NoMemoryToday,
                Message = "今天还没有记录任何记忆，用 myclaw_note 记下关键进展或决定。"
            });
        }
        else if (todayEntryCount >= DistillThreshold)
        {
            result.Nudges.Add(new MemoryNudge
            {
                Kind = MemoryNudgeKind.NeedsDistillation,
                Message = $"今日记忆已 {todayEntryCount} 条，建议蒸馏要点到 MEMORY.md 并归档当日日志。"
            });
        }

        // 3. 近期未解决问题
        var questions = ExtractOpenQuestions(_memoryStore.GetRecentMemories(recentDays));
        if (questions.Count > 0)
        {
            var body = string.Join("\n", questions.Select(q => $"  - {q}"));
            result.Nudges.Add(new MemoryNudge
            {
                Kind = MemoryNudgeKind.OpenQuestions,
                Message = $"近期仍有未解决的问题待跟进：\n{body}"
            });
        }

        return result;
    }

    /// <summary>
    /// 渲染为可读文本（供 MCP 工具/简报输出）。
    /// </summary>
    public string Render(int recentDays = 3)
    {
        var result = Generate(recentDays);
        if (!result.HasNudges)
        {
            return "记忆状态良好，暂无需要提醒的事项。";
        }

        var lines = new List<string> { "🧠 记忆提醒（nudges）：", "" };
        lines.AddRange(result.Nudges.Select(n => $"- {n.Message}"));
        return string.Join("\n", lines);
    }

    private static int CountEntries(string log)
        => string.IsNullOrEmpty(log)
            ? 0
            : log.Split('\n').Count(l => l.TrimStart().StartsWith("- [", StringComparison.Ordinal));

    private List<string> ExtractOpenQuestions(string recentLog)
    {
        if (string.IsNullOrWhiteSpace(recentLog)) return new List<string>();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var questions = new List<string>();

        foreach (var raw in recentLog.Split('\n'))
        {
            var line = raw.Trim();
            // 仅看日志条目行，跳过日期标题等
            if (!line.StartsWith("- [", StringComparison.Ordinal)) continue;
            if (!OpenQuestionMarkers.Any(m => line.Contains(m, StringComparison.OrdinalIgnoreCase))) continue;

            var text = line.Length > 100 ? line[..97] + "..." : line;
            if (seen.Add(text))
            {
                questions.Add(text);
                if (questions.Count >= MaxOpenQuestions) break;
            }
        }

        return questions;
    }
}
