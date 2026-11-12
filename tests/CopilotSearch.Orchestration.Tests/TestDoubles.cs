using CopilotSearch.Contracts;
using CopilotSearch.Llm;
using Microsoft.Extensions.AI;

namespace CopilotSearch.Orchestration.Tests;

/// <summary>Scripted IChatClient: the delegate receives the call index and the full message list.</summary>
public sealed class ScriptedChatClient(Func<int, IList<ChatMessage>, ChatOptions?, CancellationToken, Task<ChatResponse>> script) : IChatClient
{
    private int _calls;
    public int Calls => _calls;

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        script(Interlocked.Increment(ref _calls) - 1, messages.ToList(), options, cancellationToken);

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);
        foreach (var update in response.ToChatResponseUpdates())
            yield return update;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;
    public void Dispose() { }

    public static ChatResponse Text(string text) => new(new ChatMessage(ChatRole.Assistant, text));

    public static ChatResponse ToolCall(string callId, string name, Dictionary<string, object?>? args = null) =>
        new(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent(callId, name, args)]));
}

public sealed class FakeChatClientFactory(IChatClient client, string provider = "AzureOpenAI", string model = "fake-model") : IChatClientFactory
{
    public LlmClient Create(string? providerOverride = null)
    {
        if (providerOverride is "bogus") throw new UnknownProviderException(providerOverride);
        if (providerOverride is "misconfigured") throw new LlmConfigurationException("missing key");
        return new LlmClient(client, providerOverride ?? provider, model);
    }
}

/// <summary>Stand-in for the real Tools project: canned results behind the same tool names.</summary>
public sealed class StubToolProvider(Func<string, object>? behaviour = null) : IToolProvider
{
    public static readonly ResultItem Email1 = new()
    {
        Id = "mail:m1", Source = Sources.Mail, Type = "email", Title = "Q3 budget draft", Snippet = "Draft attached",
        Url = "https://example.test/m1", Score = 0.9, Author = new ResultAuthor("Priya Sharma", "priya@contoso.com"),
    };

    public static readonly ResultItem File1 = new()
    {
        Id = "files:d1/i1", Source = Sources.Files, Type = "document", Title = "Q3 Budget.xlsx", Snippet = "Budget sheet",
        Url = "https://example.test/f1", Score = 0.5,
    };

    public IReadOnlyList<AITool> GetTools() =>
    [
        Create(ToolNames.SearchEmails),
        Create(ToolNames.SearchAll),
        Create(ToolNames.GetCurrentDatetime),
    ];

    private AIFunction Create(string name) =>
        AIFunctionFactory.Create(
            (string? query = null) => behaviour?.Invoke(name) ?? DefaultFor(name),
            name,
            $"stub {name}");

    private static object DefaultFor(string name) => name switch
    {
        ToolNames.GetCurrentDatetime => new { utc_now = "2026-11-12T10:00:00Z" },
        ToolNames.SearchAll => new SearchEnvelope { Results = [Email1, File1], Returned = 2, TotalEstimate = 2 },
        _ => new SearchEnvelope { Results = [Email1], Returned = 1, TotalEstimate = 1 },
    };
}

