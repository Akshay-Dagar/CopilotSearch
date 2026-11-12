using CopilotSearch.Contracts;
using CopilotSearch.Llm;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CopilotSearch.Orchestration.Tests;

public class SearchOrchestratorTests
{
    private static SearchOrchestrator Create(
        IChatClient client, IToolProvider? tools = null, int budgetSeconds = 30, int maxIterations = 6, int maxToolChars = 32000) =>
        new(new FakeChatClientFactory(client),
            tools ?? new StubToolProvider(),
            Options.Create(new LlmOptions { MaxIterations = maxIterations }),
            Options.Create(new OrchestrationOptions { BudgetSeconds = budgetSeconds, MaxToolOutputChars = maxToolChars }),
            NullLogger<SearchOrchestrator>.Instance);

    private static async Task<List<SearchEvent>> Collect(SearchOrchestrator o, string query = "q3 budget", string? provider = null)
    {
        var events = new List<SearchEvent>();
        await foreach (var e in o.RunAsync(new SearchRunRequest(query, provider), CancellationToken.None))
            events.Add(e);
        return events;
    }

    [Fact]
    public async Task HappyPath_StreamsEventsInOrder_AndResolvesCitations()
    {
        var client = new ScriptedChatClient((i, _, _, _) => Task.FromResult(i switch
        {
            0 => ScriptedChatClient.ToolCall("c1", ToolNames.SearchEmails, new() { ["query"] = "q3 budget" }),
            _ => ScriptedChatClient.Text("Priya sent the draft [mail:m1]. Made up claim [mail:ghost]."),
        }));

        var events = await Collect(Create(client));

        Assert.Equal(
            ["run_started", "tool_call", "tool_result", "answer_delta", "results", "final"],
            events.Select(e => e.Name).Distinct().ToArray());

        var started = Assert.IsType<RunStartedEvent>(events[0]);
        Assert.Equal("fake-model", started.Model);

        var call = events.OfType<ToolCallEvent>().Single();
        Assert.Equal(ToolNames.SearchEmails, call.Tool);
        Assert.Equal("c1", call.CallId);

        var result = events.OfType<ToolResultEvent>().Single();
        Assert.True(result.Ok);
        Assert.Equal(1, result.Returned);
        Assert.Equal("c1", result.CallId);

        var final = events.OfType<FinalEvent>().Single();
        Assert.Equal("Priya sent the draft [1]. Made up claim.", final.Answer);
        var citation = Assert.Single(final.Citations);
        Assert.Equal("mail:m1", citation.Id);
        Assert.Equal("https://example.test/m1", citation.Url);

        Assert.Equal("mail:m1", events.OfType<ResultsEvent>().Single().Results[0].Id);
        Assert.IsType<FinalEvent>(events[^1]);
    }

    [Fact]
    public async Task ParallelToolCalls_AreAllExecutedAndRegistered()
    {
        var client = new ScriptedChatClient((i, _, _, _) => Task.FromResult(i == 0
            ? new ChatResponse(new ChatMessage(ChatRole.Assistant,
            [
                new FunctionCallContent("a", ToolNames.SearchEmails),
                new FunctionCallContent("b", ToolNames.SearchAll),
            ]))
            : ScriptedChatClient.Text("Found it [files:d1/i1]")));

        var events = await Collect(Create(client));

        Assert.Equal(2, events.OfType<ToolResultEvent>().Count());
        var results = events.OfType<ResultsEvent>().Single().Results;
        Assert.Equal(["files:d1/i1", "mail:m1"], results.Select(r => r.Id).ToArray()); // cited first, then by score
    }

    [Fact]
    public async Task ToolThrowing_BecomesErrorResult_AndRunContinues()
    {
        var tools = new ThrowingToolProvider();
        var client = new ScriptedChatClient((i, messages, _, _) => Task.FromResult(i == 0
            ? ScriptedChatClient.ToolCall("c1", ToolNames.SearchEmails)
            : ScriptedChatClient.Text("The mail source failed.")));

        var events = await Collect(Create(client, tools));

        var result = events.OfType<ToolResultEvent>().Single();
        Assert.False(result.Ok);
        Assert.Equal(ToolErrorCodes.UpstreamError, result.ErrorCode);
        Assert.Equal("The mail source failed.", events.OfType<FinalEvent>().Single().Answer);
    }

    [Fact]
    public async Task ErrorEnvelopeFromTool_IsReportedAsNotOk()
    {
        var tools = new StubToolProvider(_ => ErrorEnvelope.Create(ToolErrorCodes.Timeout, "slow", retryable: true));
        var client = new ScriptedChatClient((i, _, _, _) => Task.FromResult(i == 0
            ? ScriptedChatClient.ToolCall("c1", ToolNames.SearchEmails)
            : ScriptedChatClient.Text("Mail timed out.")));

        var events = await Collect(Create(client, tools));

        var result = events.OfType<ToolResultEvent>().Single();
        Assert.False(result.Ok);
        Assert.Equal(ToolErrorCodes.Timeout, result.ErrorCode);
    }

    [Fact]
    public async Task OversizedToolOutput_IsTruncatedBeforeReachingTheModel()
    {
        var big = Enumerable.Range(0, 50).Select(n => StubToolProvider.Email1 with { Id = $"mail:{n}", Snippet = new string('x', 300) }).ToList();
        var tools = new StubToolProvider(_ => new SearchEnvelope { Results = big, Returned = 50, TotalEstimate = 50 });
        string? toolMessage = null;
        var client = new ScriptedChatClient((i, messages, _, _) =>
        {
            if (i == 0) return Task.FromResult(ScriptedChatClient.ToolCall("c1", ToolNames.SearchEmails));
            toolMessage = messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().Single().Result?.ToString();
            return Task.FromResult(ScriptedChatClient.Text("ok"));
        });

        await Collect(Create(client, tools, maxToolChars: 5000));

        Assert.NotNull(toolMessage);
        Assert.True(toolMessage!.Length <= 6000, $"len={toolMessage.Length}");
        Assert.Contains("\"truncated\":true", toolMessage);
    }

    [Fact]
    public async Task IterationCap_FallsBackToAnswerFromCollectedResults()
    {
        var client = new ScriptedChatClient((i, messages, options, _) =>
        {
            var isFallback = messages.Any(m => m.Role == ChatRole.System && m.Text.StartsWith("You could not finish"));
            return Task.FromResult(isFallback
                ? ScriptedChatClient.Text("Best effort: [mail:m1]")
                : ScriptedChatClient.ToolCall($"c{i}", ToolNames.SearchEmails));
        });

        var events = await Collect(Create(client, maxIterations: 2));

        var final = events.OfType<FinalEvent>().Single();
        Assert.Equal("Best effort: [1]", final.Answer);
        Assert.True(events.OfType<ToolResultEvent>().Count() <= 3);
    }

    [Fact]
    public async Task BudgetExceeded_EmitsErrorEvent()
    {
        var client = new ScriptedChatClient(async (_, _, _, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return ScriptedChatClient.Text("never");
        });

        var events = await Collect(Create(client, budgetSeconds: 1));

        var error = Assert.IsType<ErrorEvent>(events[^1]);
        Assert.Equal(RunErrorCodes.BudgetExceeded, error.Code);
        Assert.DoesNotContain(events, e => e is FinalEvent);
    }

    [Fact]
    public async Task CallerCancellation_EmitsCancelledError()
    {
        var client = new ScriptedChatClient(async (_, _, _, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return ScriptedChatClient.Text("never");
        });
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var events = new List<SearchEvent>();

        await foreach (var e in Create(client).RunAsync(new SearchRunRequest("x"), cts.Token))
            events.Add(e);

        Assert.Equal(RunErrorCodes.Cancelled, Assert.IsType<ErrorEvent>(events[^1]).Code);
    }

    [Fact]
    public async Task LlmFailure_EmitsErrorEvent()
    {
        var client = new ScriptedChatClient((_, _, _, _) => throw new InvalidOperationException("boom"));

        var events = await Collect(Create(client));

        var error = Assert.IsType<ErrorEvent>(events[^1]);
        Assert.Equal(RunErrorCodes.LlmFailure, error.Code);
        Assert.Equal("boom", error.Message);
    }

    [Theory]
    [InlineData("bogus", RunErrorCodes.InvalidProvider)]
    [InlineData("misconfigured", RunErrorCodes.Configuration)]
    public async Task ProviderProblems_EmitSingleErrorEvent(string provider, string code)
    {
        var client = new ScriptedChatClient((_, _, _, _) => Task.FromResult(ScriptedChatClient.Text("x")));

        var events = await Collect(Create(client), provider: provider);

        var error = Assert.IsType<ErrorEvent>(Assert.Single(events));
        Assert.Equal(code, error.Code);
        Assert.Equal(0, client.Calls);
    }

    [Fact]
    public async Task ProviderOverride_IsReportedInRunStarted()
    {
        var client = new ScriptedChatClient((_, _, _, _) => Task.FromResult(ScriptedChatClient.Text("hi")));

        var events = await Collect(Create(client), provider: "Claude");

        Assert.Equal("Claude", Assert.IsType<RunStartedEvent>(events[0]).Provider);
    }

    [Fact]
    public async Task SystemPromptAndTools_ArePassedToTheModel()
    {
        ChatOptions? seen = null;
        var client = new ScriptedChatClient((_, _, options, _) =>
        {
            seen = options;
            return Task.FromResult(ScriptedChatClient.Text("hi"));
        });

        await Collect(Create(client));

        Assert.NotNull(seen);
        Assert.Contains("enterprise search assistant", seen!.Instructions);
        Assert.Contains("untrusted DATA", seen.Instructions);
        Assert.Equal(3, seen.Tools!.Count);
    }

    private sealed class ThrowingToolProvider : IToolProvider
    {
        private static object Throw(string? query = null) => throw new InvalidOperationException("graph down");

        public IReadOnlyList<AITool> GetTools() =>
        [
            AIFunctionFactory.Create(Throw, ToolNames.SearchEmails, "throws"),
        ];
    }
}

