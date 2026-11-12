using System.Text.Json;
using CopilotSearch.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CopilotSearch.Tools.Tests;

public class SearchAllTests
{
    private static SearchHit MailHit(string id, double rank) => new() { EntityType = SearchEntityTypes.Message, Rank = rank, Message = FakeConnectors.Msg(id) };
    private static SearchHit FileHit(string id, double rank) => new() { EntityType = SearchEntityTypes.DriveItem, Rank = rank, DriveItem = FakeConnectors.Drive(id), Summary = "Matched <c0>budget</c0> text" };
    private static SearchHit TeamsHit(string id, double rank) => new() { EntityType = SearchEntityTypes.ChatMessage, Rank = rank, ChatMessage = FakeConnectors.Chat(id) };
    private static SearchHit EventHit(string id, double rank) => new() { EntityType = SearchEntityTypes.Event, Rank = rank, Event = FakeConnectors.Event(id) };

    [Fact]
    public async Task MergesSourcesAndSortsByScoreDescending()
    {
        var f = new FakeConnectors { SearchResult = new([MailHit("m1", 0.3), FileHit("f1", 0.9), TeamsHit("t1", 0.5), EventHit("e1", 0.7), MailHit("m2", 0.6)], 5) };
        var env = Assert.IsType<SearchEnvelope>(await T.All(f).SearchAll("budget"));

        Assert.Equal(["files:f1", "calendar:e1", "mail:m2", "teams:t1", "mail:m1"], env.Results.Select(r => r.Id));
        Assert.Equal(5, env.Returned);
        Assert.False(env.Truncated);
        Assert.Equal("Matched <c0>budget</c0> text", env.Results[0].Snippet);
        Assert.Equal("event", env.Results[1].Type);
        Assert.Equal(4, f.SearchRequests.Count);
        Assert.All(f.SearchRequests, r => Assert.Equal(10, r.Top));
    }

    [Fact]
    public async Task SourcesMapToEntityTypes_AndUnrequestedHitsAreDropped()
    {
        var f = new FakeConnectors { SearchResult = new([MailHit("m1", 0.3), FileHit("f1", 0.9)], 2) };
        var env = Assert.IsType<SearchEnvelope>(await T.All(f).SearchAll("x", sources: ["Mail", "mail"]));
        Assert.Equal([SearchEntityTypes.Message], f.LastSearch!.EntityTypes);
        Assert.Equal("mail:m1", Assert.Single(env.Results).Id);
    }

    [Fact]
    public async Task AppliesPerSourceCap()
    {
        var hits = Enumerable.Range(0, 6).Select(i => MailHit($"m{i}", i)).ToList();
        var f = new FakeConnectors { SearchResult = new(hits, 6) };
        var env = Assert.IsType<SearchEnvelope>(await T.All(f).SearchAll("x", top: 2));
        Assert.Equal(2, env.Returned);
        Assert.True(env.Truncated);
        Assert.Equal(6, env.TotalEstimate);
        Assert.Equal("mail:m5", env.Results[0].Id);
    }

    [Fact]
    public async Task ValidatesArguments()
    {
        var f = new FakeConnectors();
        T.Err(await T.All(f).SearchAll(" "), ToolErrorCodes.InvalidArgument);
        var bad = T.Err(await T.All(f).SearchAll("x", sources: ["copilot"]), ToolErrorCodes.InvalidArgument);
        Assert.Contains("calendar", bad.Error.Message);
        T.Err(await T.All(f).SearchAll("x", start: "nope"), ToolErrorCodes.InvalidArgument);
        await T.All(f).SearchAll("x", top: 99);
        Assert.Equal(25, f.LastSearch!.Top);
    }

    [Fact]
    public async Task Timeout_And_Upstream()
    {
        var slow = new FakeConnectors { Delay = TimeSpan.FromSeconds(5) };
        T.Err(await T.All(slow, o => o.TimeoutSeconds[ToolNames.SearchAll] = 0).SearchAll("x"), ToolErrorCodes.Timeout);
        T.Err(await T.All(new FakeConnectors { Throw = new Exception("x") }).SearchAll("x"), ToolErrorCodes.UpstreamError);
    }
}

public class ToolProviderTests
{
    private static ServiceProvider Build(Dictionary<string, string?>? config = null)
    {
        var f = new FakeConnectors();
        var services = new ServiceCollection();
        services.AddSingleton<IPeopleConnector>(f).AddSingleton<ISearchConnector>(f).AddSingleton<IMailConnector>(f)
            .AddSingleton<ITeamsConnector>(f).AddSingleton<IFilesConnector>(f).AddSingleton<ICopilotChatConnector>(f);
        services.AddSearchTools(new ConfigurationBuilder().AddInMemoryCollection(config ?? []).Build());
        return services.BuildServiceProvider(validateScopes: true);
    }

    [Fact]
    public void ExposesExactlyTheContractToolNames()
    {
        using var sp = Build();
        var tools = sp.GetRequiredService<IToolProvider>().GetTools();
        Assert.Equal(ToolNames.All.OrderBy(n => n), tools.Select(t => t.Name).OrderBy(n => n));
        Assert.All(tools, t => Assert.False(string.IsNullOrWhiteSpace(t.Description)));
    }

    [Fact]
    public void EveryParameterHasADescription()
    {
        using var sp = Build();
        foreach (var fn in sp.GetRequiredService<IToolProvider>().GetTools().Cast<AIFunction>())
        {
            var props = fn.JsonSchema.TryGetProperty("properties", out var p) ? p : default;
            if (props.ValueKind != JsonValueKind.Object) continue;
            foreach (var prop in props.EnumerateObject())
                Assert.True(prop.Value.TryGetProperty("description", out _), $"{fn.Name}.{prop.Name} lacks a description");
        }
    }

    [Fact]
    public async Task ToolsInvokeThroughAIFunction_AndSerializeSnakeCase()
    {
        using var sp = Build();
        var tools = sp.GetRequiredService<IToolProvider>().GetTools().Cast<AIFunction>().ToDictionary(t => t.Name);

        var user = await tools[ToolNames.GetCurrentUser].InvokeAsync(new AIFunctionArguments());
        Assert.Contains("\"display_name\"", JsonSerializer.Serialize(user));

        var err = await tools[ToolNames.SearchEmails].InvokeAsync(new AIFunctionArguments());
        var json = JsonSerializer.Serialize(err);
        Assert.Contains("\"invalid_argument\"", json);

        var ok = await tools[ToolNames.SearchEmails].InvokeAsync(new AIFunctionArguments { ["query"] = "x", ["top"] = 3 });
        Assert.Contains("\"total_estimate\"", JsonSerializer.Serialize(ok));
    }

    [Fact]
    public void BindsToolOptionsFromConfiguration_AndRegistersDemoUser()
    {
        using var sp = Build(new() { ["Tools:DefaultTimeoutSeconds"] = "7", ["Tools:TimeoutSeconds:get_file_content"] = "45", ["Tools:MaxTop"] = "12" });
        var o = sp.GetRequiredService<IOptions<ToolOptions>>().Value;
        Assert.Equal(TimeSpan.FromSeconds(7), o.TimeoutFor(ToolNames.SearchEmails));
        Assert.Equal(TimeSpan.FromSeconds(45), o.TimeoutFor(ToolNames.GetFileContent));
        Assert.Equal(12, o.MaxTop);
        Assert.IsType<DemoUserContext>(sp.GetRequiredService<IUserContext>());
    }
}
