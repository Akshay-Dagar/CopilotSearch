using System.Text.Json;
using CopilotSearch.Contracts;

namespace CopilotSearch.Tools.Tests;

public class IdentityToolsTests
{
    [Fact]
    public async Task GetCurrentUser_MapsProfile()
    {
        var r = Assert.IsType<CurrentUserResult>(await T.Identity(new FakeConnectors()).GetCurrentUser());
        Assert.Equal("akshay@contoso.com", r.Email);
        Assert.Equal("Maya Rao", r.Manager!.DisplayName);
        Assert.Equal("Asia/Kolkata", r.TimeZone);

        var json = JsonSerializer.Serialize<object>(r);
        Assert.Contains("\"display_name\"", json);
        Assert.Contains("\"time_zone\"", json);
    }

    [Fact]
    public async Task GetCurrentUser_UpstreamError()
    {
        var f = new FakeConnectors { Throw = new InvalidOperationException("boom") };
        var e = T.Err(await T.Identity(f).GetCurrentUser(), ToolErrorCodes.UpstreamError);
        Assert.Contains("boom", e.Error.Message);
        Assert.False(e.Error.Retryable);
    }

    [Fact]
    public async Task GetCurrentDatetime_ComputesWeekInUserTimezone()
    {
        // Wed 2026-11-11 22:00 UTC is Thu 03:30 in Kolkata.
        var now = new DateTimeOffset(2026, 11, 11, 22, 0, 0, TimeSpan.Zero);
        var r = Assert.IsType<CurrentDatetimeResult>(await T.Identity(new FakeConnectors(), now).GetCurrentDatetime());
        Assert.Equal("Asia/Kolkata", r.Timezone);
        Assert.Equal("Thursday", r.DayOfWeek);
        Assert.Equal(new DateTimeOffset(2026, 11, 9, 0, 0, 0, TimeSpan.FromHours(5.5)), r.WeekStart);
        Assert.Equal(new DateTimeOffset(2026, 11, 15, 23, 59, 59, TimeSpan.FromHours(5.5)), r.WeekEnd);
        Assert.Equal(now, r.UtcNow);
    }

    [Fact]
    public async Task GetCurrentDatetime_HonoursExplicitTimezone_AndSundayBelongsToPreviousMonday()
    {
        var now = new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero); // Sunday
        var r = Assert.IsType<CurrentDatetimeResult>(await T.Identity(new FakeConnectors(), now).GetCurrentDatetime("UTC"));
        Assert.Equal("Sunday", r.DayOfWeek);
        Assert.Equal(new DateTimeOffset(2026, 11, 9, 0, 0, 0, TimeSpan.Zero), r.WeekStart);
        Assert.Equal(new DateTimeOffset(2026, 11, 15, 23, 59, 59, TimeSpan.Zero), r.WeekEnd);
    }

    [Fact]
    public async Task GetCurrentDatetime_InvalidTimezone()
    {
        T.Err(await T.Identity(new FakeConnectors()).GetCurrentDatetime("Mars/Olympus"), ToolErrorCodes.InvalidArgument);
    }

    [Fact]
    public async Task ResolvePerson_ReturnsMatches_AndClampsTop()
    {
        var f = new FakeConnectors();
        for (var i = 0; i < 20; i++)
            f.People.Add(new Person { Id = $"p{i}", DisplayName = $"Priya {i}", Mail = $"p{i}@contoso.com", UserPrincipalName = $"p{i}@contoso.com", Score = 1 });
        var tool = T.Identity(f);

        var r = Assert.IsType<ResolvePersonResult>(await tool.ResolvePerson("priya"));
        Assert.Equal(5, r.People.Count);
        Assert.Equal(5, f.LastResolveTop);

        r = Assert.IsType<ResolvePersonResult>(await tool.ResolvePerson("priya", 99));
        Assert.Equal(10, r.People.Count);
        await tool.ResolvePerson("priya", 0);
        Assert.Equal(1, f.LastResolveTop);
        Assert.Contains("\"people\"", JsonSerializer.Serialize<object>(r));
    }

    [Fact]
    public async Task ResolvePerson_BlankName_IsInvalid()
    {
        T.Err(await T.Identity(new FakeConnectors()).ResolvePerson("  "), ToolErrorCodes.InvalidArgument);
    }

    [Fact]
    public async Task Timeout_ReturnsRetryableEnvelope()
    {
        var f = new FakeConnectors { Delay = TimeSpan.FromSeconds(5) };
        var tool = T.Identity(f, cfg: o => o.TimeoutSeconds[ToolNames.ResolvePerson] = 0);
        var e = T.Err(await tool.ResolvePerson("x"), ToolErrorCodes.Timeout);
        Assert.True(e.Error.Retryable);
    }

    [Fact]
    public async Task CallerCancellation_Propagates()
    {
        var f = new FakeConnectors { Delay = TimeSpan.FromSeconds(5) };
        using var cts = new CancellationTokenSource(50);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => T.Identity(f).ResolvePerson("x", null, cts.Token));
    }
}
