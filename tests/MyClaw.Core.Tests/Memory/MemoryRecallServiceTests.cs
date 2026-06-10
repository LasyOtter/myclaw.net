using MyClaw.Core.Memory;

namespace MyClaw.Core.Tests.Memory;

public class MemoryRecallServiceTests : IDisposable
{
    private readonly string _workspace;
    private readonly string _memoryDir;
    private readonly string _archivedDir;
    private readonly MemoryRecallService _service;

    public MemoryRecallServiceTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), $"myclaw_recall_test_{Guid.NewGuid()}");
        _memoryDir = Path.Combine(_workspace, "memory");
        _archivedDir = Path.Combine(_memoryDir, "archived");
        Directory.CreateDirectory(_archivedDir);
        _service = new MemoryRecallService(_workspace);
    }

    public void Dispose()
    {
        if (Directory.Exists(_workspace))
        {
            Directory.Delete(_workspace, true);
        }
    }

    [Fact]
    public async Task RecallAsync_EmptyQuery_ReturnsEmpty()
    {
        var result = await _service.RecallAsync("   ");

        Assert.Empty(result.Hits);
        Assert.Equal(0, result.DocumentsScanned);
    }

    [Fact]
    public async Task RecallAsync_NoMemoryFiles_ReturnsEmpty()
    {
        var result = await _service.RecallAsync("anything");

        Assert.Empty(result.Hits);
        Assert.Equal(0, result.DocumentsScanned);
    }

    [Fact]
    public async Task RecallAsync_MatchesContentInArchivedLog()
    {
        File.WriteAllText(
            Path.Combine(_archivedDir, "2026-01-01.md"),
            "- [09:00:00] Decided to migrate the database to PostgreSQL for the analytics pipeline.");

        var result = await _service.RecallAsync("postgresql database migration", topK: 5);

        Assert.NotEmpty(result.Hits);
        Assert.Contains(result.Hits, h => h.Source.Contains("2026-01-01.md") && h.Source.Contains("归档"));
        Assert.Contains(result.Hits, h => h.Snippet.Contains("PostgreSQL"));
    }

    [Fact]
    public async Task RecallAsync_ScansAllSources_LongTermDailyAndArchived()
    {
        File.WriteAllText(Path.Combine(_workspace, "MEMORY.md"), "## 长期记忆\nProject uses .NET 9.");
        File.WriteAllText(Path.Combine(_memoryDir, "2026-06-10.md"), "- [10:00:00] today note about widgets");
        File.WriteAllText(Path.Combine(_archivedDir, "2026-05-01.md"), "- [11:00:00] archived note about widgets");

        var result = await _service.RecallAsync("widgets");

        Assert.Equal(3, result.DocumentsScanned);
        Assert.NotEmpty(result.Hits);
    }

    [Fact]
    public async Task RecallAsync_KeywordFallback_WhenSemanticMisses()
    {
        // A highly specific token unlikely to be semantically surfaced but present verbatim.
        File.WriteAllText(
            Path.Combine(_archivedDir, "2026-02-02.md"),
            "- [12:00:00] the deployment token is ZZQX9981 keep it safe");

        var result = await _service.RecallAsync("ZZQX9981");

        Assert.NotEmpty(result.Hits);
        Assert.Contains(result.Hits, h => h.Snippet.Contains("ZZQX9981"));
    }

    [Fact]
    public async Task RecallAsync_RespectsTopK()
    {
        for (int i = 1; i <= 10; i++)
        {
            File.WriteAllText(
                Path.Combine(_archivedDir, $"2026-03-{i:D2}.md"),
                $"- [08:00:00] meeting notes entry number {i} about budget planning");
        }

        var result = await _service.RecallAsync("budget planning meeting", topK: 3);

        Assert.True(result.Hits.Count <= 3);
    }
}
