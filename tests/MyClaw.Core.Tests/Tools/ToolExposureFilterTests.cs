using MyClaw.Core.Tools;
using Xunit;

namespace MyClaw.Core.Tests.Tools;

public class ToolExposureFilterTests
{
    [Fact]
    public void EmptyLists_IsUnrestricted_AllowsEverything()
    {
        var filter = new ToolExposureFilter(null, null);

        Assert.True(filter.IsUnrestricted);
        Assert.True(filter.IsAllowed("myclaw_exec"));
        Assert.True(filter.IsAllowed("anything"));
    }

    [Fact]
    public void AllowList_OnlyExposesListedTools()
    {
        var filter = new ToolExposureFilter(new[] { "myclaw_read", "myclaw_recall" }, null);

        Assert.False(filter.IsUnrestricted);
        Assert.True(filter.IsAllowed("myclaw_read"));
        Assert.True(filter.IsAllowed("myclaw_recall"));
        Assert.False(filter.IsAllowed("myclaw_exec"));
    }

    [Fact]
    public void BlockList_RemovesListedTools()
    {
        var filter = new ToolExposureFilter(null, new[] { "myclaw_exec" });

        Assert.True(filter.IsAllowed("myclaw_read"));
        Assert.False(filter.IsAllowed("myclaw_exec"));
    }

    [Fact]
    public void BlockList_TakesPrecedenceOverAllowList()
    {
        var filter = new ToolExposureFilter(
            allowedTools: new[] { "myclaw_exec", "myclaw_read" },
            blockedTools: new[] { "myclaw_exec" });

        Assert.False(filter.IsAllowed("myclaw_exec"));
        Assert.True(filter.IsAllowed("myclaw_read"));
    }

    [Fact]
    public void IsAllowed_IsCaseInsensitive()
    {
        var filter = new ToolExposureFilter(new[] { "myclaw_read" }, new[] { "myclaw_exec" });

        Assert.True(filter.IsAllowed("MyClaw_Read"));
        Assert.False(filter.IsAllowed("MYCLAW_EXEC"));
    }

    [Fact]
    public void IsAllowed_RejectsEmptyName()
    {
        var filter = new ToolExposureFilter(null, null);

        Assert.False(filter.IsAllowed(""));
        Assert.False(filter.IsAllowed("   "));
    }

    [Fact]
    public void Filter_KeepsOnlyAllowed()
    {
        var filter = new ToolExposureFilter(null, new[] { "b" });
        var result = filter.Filter(new[] { "a", "b", "c" });

        Assert.Equal(new[] { "a", "c" }, result);
    }
}
