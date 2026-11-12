using CopilotSearch.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CopilotSearch.Tools.Tests;

public class SearchAllScopingTests
{
    private static SearchHit MailHit(string id, double rank) => new() { EntityType = SearchEntityTypes.Message, Rank = rank, Message = FakeConnectors.Msg(id) };
    private static SearchHit FileHit(string id, double rank) => new() { EntityType = SearchEntityTypes.DriveItem, Rank = rank, DriveItem = FakeConnectors.Drive(id) };
    private static SearchHit EventHit(string id, double rank) => new() { EntityType = SearchEntityTypes.Event, Rank = rank, Event = FakeConnectors.Event(id) };

    [Fact]
    public async Task TopIsPerSource_AndMergedListStaysSortedByScore()
    {
        var hits = new List<SearchHit>();
        for (var i = 0; i < 5; i++) { hits.Add(MailHit($"m{i}", 0.1 + i * 0.1)); hits.Add(FileHit($"f{i}", 0.15 + i * 0.1)); }
        var f = new FakeConnectors { SearchResult = new(hits, 10) };
        var env = Assert.IsType<SearchEnvelope>(await T.All(f).SearchAll("x", sources: ["mail", "files"], top: 3));
        Assert.Equal(6, env.Returned);
        Assert.Equal(3, env.Results.Count(r => r.Source == "mail"));
        Assert.Equal(3, env.Results.Count(r => r.Source == "files"));
        Assert.Equal(env.Results.OrderByDescending(r => r.Score).Select(r => r.Id), env.Results.Select(r => r.Id));
        Assert.Equal(10, env.TotalEstimate);
        Assert.True(env.Truncated);
        Assert.All(f.SearchRequests, r => Assert.Equal(3, r.Top));
    }

    [Theory]
    [InlineData("calendar")]
    [InlineData("events")]
    [InlineData("Events")]
    public async Task CalendarAndEventsAreInterchangeable(string source)
    {
        var f = new FakeConnectors { SearchResult = new([EventHit("e1", 0.7)], 1) };
        var env = Assert.IsType<SearchEnvelope>(await T.All(f).SearchAll("x", sources: [source]));
        Assert.Equal("calendar:e1", Assert.Single(env.Results).Id);
        Assert.Contains("sources=calendar", env.QueryUsed);
        Assert.Equal([SearchEntityTypes.Event], f.LastSearch!.EntityTypes);
    }

    [Fact]
    public async Task QueryUsedUsesInvariantIso8601()
    {
        var f = new FakeConnectors();
        var env = Assert.IsType<SearchEnvelope>(await T.All(f).SearchAll("x", start: "2026-11-05T10:30:00Z"));
        Assert.Contains("start=2026-11-05T10:30:00Z", env.QueryUsed);
        var mail = Assert.IsType<SearchEnvelope>(await T.Mail(f).SearchEmails(has_attachments: true, start: "2026-11-05T10:30:00.5Z"));
        Assert.Contains("has_attachments=true", mail.QueryUsed);
        Assert.Contains("start=2026-11-05T10:30:00.5Z", mail.QueryUsed);
    }

    [Fact]
    public async Task DateOnlyInputs_UseUserTimezone_EndInclusive()
    {
        var f = new FakeConnectors();
        await T.Mail(f).SearchEmails(start: "2026-11-03", end: "2026-11-03");
        var kolkata = TimeSpan.FromHours(5.5);
        Assert.Equal(new DateTimeOffset(2026, 11, 3, 0, 0, 0, kolkata), f.LastMail!.Range.Start);
        Assert.Equal(new DateTimeOffset(2026, 11, 3, 23, 59, 59, 999, kolkata), f.LastMail.Range.End);

        await T.Files(f).SearchFiles(start: "2026-11-03T00:00:00Z", end: "2026-11-03T05:00:00+02:00");
        Assert.Equal(new DateTimeOffset(2026, 11, 3, 0, 0, 0, TimeSpan.Zero), f.LastFiles!.Range.Start);
        Assert.Equal(new DateTimeOffset(2026, 11, 3, 3, 0, 0, TimeSpan.Zero), f.LastFiles.Range.End);
    }

    [Fact]
    public async Task SameDateOnlyStartAndEnd_IsValid_ReversedIsNot()
    {
        T.Err(await T.Chats(new FakeConnectors()).SearchCopilotChats(start: "2026-11-04", end: "2026-11-03"), ToolErrorCodes.InvalidArgument);
        Assert.IsType<SearchEnvelope>(await T.Chats(new FakeConnectors()).SearchCopilotChats(start: "2026-11-03", end: "2026-11-03"));
    }
}

public class ArgumentBindingTests
{
    private static ServiceProvider Build()
    {
        var f = new FakeConnectors();
        var services = new ServiceCollection();
        services.AddSingleton<IPeopleConnector>(f).AddSingleton<ISearchConnector>(f).AddSingleton<IMailConnector>(f)
            .AddSingleton<ITeamsConnector>(f).AddSingleton<IFilesConnector>(f).AddSingleton<ICopilotChatConnector>(f);
        services.AddSearchTools(new ConfigurationBuilder().Build());
        return services.BuildServiceProvider();
    }

    private static async Task<ErrorEnvelope> Invoke(string tool, Dictionary<string, object?> args)
    {
        using var sp = Build();
        var fn = sp.GetRequiredService<IToolProvider>().GetTools().Cast<AIFunction>().Single(t => t.Name == tool);
        return Assert.IsType<ErrorEnvelope>(await fn.InvokeAsync(new AIFunctionArguments(args)));
    }

    [Fact]
    public async Task MissingRequiredArgument_NamesParameter()
    {
        var e = await Invoke(ToolNames.GetEmail, new());
        Assert.Equal(ToolErrorCodes.InvalidArgument, e.Error.Code);
        Assert.Contains("'message_id'", e.Error.Message);

        e = await Invoke(ToolNames.ResolvePerson, new() { ["name"] = null });
        Assert.Contains("'name'", e.Error.Message);
    }

    [Theory]
    [InlineData(ToolNames.SearchEmails, "top", "abc")]
    [InlineData(ToolNames.SearchEmails, "query", 123)]
    [InlineData(ToolNames.SearchEmails, "has_attachments", "maybe")]
    [InlineData(ToolNames.SearchAll, "sources", "mail")]
    public async Task WrongTypedArgument_NamesParameter(string tool, string param, object value)
    {
        var e = await Invoke(tool, new() { ["query"] = "x", [param] = value });
        Assert.Equal(ToolErrorCodes.InvalidArgument, e.Error.Code);
        Assert.Contains($"'{param}'", e.Error.Message);
    }

    [Fact]
    public async Task WellTypedArgumentsStillRun()
    {
        using var sp = Build();
        var fn = sp.GetRequiredService<IToolProvider>().GetTools().Cast<AIFunction>().Single(t => t.Name == ToolNames.SearchEmails);
        Assert.Contains("\"results\"", (await fn.InvokeAsync(new AIFunctionArguments { ["query"] = "x", ["top"] = 3, ["is_unread"] = true }))!.ToString());
    }

    [Fact]
    public void SchemaExposesEnums()
    {
        using var sp = Build();
        var fn = sp.GetRequiredService<IToolProvider>().GetTools().Cast<AIFunction>().Single(t => t.Name == ToolNames.SearchEmails);
        var folder = fn.JsonSchema.GetProperty("properties").GetProperty("folder").GetProperty("enum");
        Assert.Contains("archive", folder.EnumerateArray().Select(e => e.GetString()));
    }
}
