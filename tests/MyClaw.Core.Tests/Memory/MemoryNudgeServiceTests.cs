using MyClaw.Core.Memory;
using Xunit;

namespace MyClaw.Core.Tests.Memory;

public class MemoryNudgeServiceTests : IDisposable
{
    private readonly string _workspace;

    public MemoryNudgeServiceTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "myclaw-nudge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_workspace, recursive: true); } catch { /* ignore */ }
    }

    private MemoryNudgeService NewService() => new(new MemoryStore(_workspace));

    private void SeedLongTerm(string content)
        => File.WriteAllText(Path.Combine(_workspace, "MEMORY.md"), content);

    [Fact]
    public void EmptyWorkspace_NudgesEmptyLongTermAndNoMemoryToday()
    {
        var result = NewService().Generate();

        Assert.True(result.HasNudges);
        Assert.Contains(result.Nudges, n => n.Kind == MemoryNudgeKind.EmptyLongTerm);
        Assert.Contains(result.Nudges, n => n.Kind == MemoryNudgeKind.NoMemoryToday);
    }

    [Fact]
    public void HealthyState_NoNudges()
    {
        SeedLongTerm("## 项目\n稳定事实若干。");
        var store = new MemoryStore(_workspace);
        store.AppendToday("完成构建并通过测试");

        var result = new MemoryNudgeService(store).Generate();

        Assert.False(result.HasNudges);
    }

    [Fact]
    public void ManyEntriesToday_NudgesDistillation_NotNoMemoryToday()
    {
        SeedLongTerm("## 项目\nx");
        var store = new MemoryStore(_workspace);
        for (int i = 0; i < MemoryNudgeService.DistillThreshold; i++)
        {
            store.AppendToday($"进展 {i}");
        }

        var result = new MemoryNudgeService(store).Generate();

        Assert.Contains(result.Nudges, n => n.Kind == MemoryNudgeKind.NeedsDistillation);
        Assert.DoesNotContain(result.Nudges, n => n.Kind == MemoryNudgeKind.NoMemoryToday);
    }

    [Fact]
    public void OpenQuestionInRecentLog_NudgesOpenQuestions()
    {
        SeedLongTerm("## 项目\nx");
        var store = new MemoryStore(_workspace);
        store.AppendToday("TODO: 跟进部署脚本");

        var result = new MemoryNudgeService(store).Generate();

        var nudge = Assert.Single(result.Nudges, n => n.Kind == MemoryNudgeKind.OpenQuestions);
        Assert.Contains("部署脚本", nudge.Message);
    }

    [Fact]
    public void OpenQuestions_CappedAtMax()
    {
        SeedLongTerm("## 项目\nx");
        var store = new MemoryStore(_workspace);
        for (int i = 0; i < MemoryNudgeService.MaxOpenQuestions + 3; i++)
        {
            store.AppendToday($"TODO 待办项 {i}");
        }

        var result = new MemoryNudgeService(store).Generate();
        var nudge = Assert.Single(result.Nudges, n => n.Kind == MemoryNudgeKind.OpenQuestions);

        var listed = nudge.Message.Split('\n').Count(l => l.TrimStart().StartsWith("- ", StringComparison.Ordinal));
        Assert.Equal(MemoryNudgeService.MaxOpenQuestions, listed);
    }

    [Fact]
    public void Render_NoNudges_ReturnsHealthyMessage()
    {
        SeedLongTerm("## 项目\nx");
        var store = new MemoryStore(_workspace);
        store.AppendToday("完成构建");

        var text = new MemoryNudgeService(store).Render();

        Assert.Contains("良好", text);
    }
}
