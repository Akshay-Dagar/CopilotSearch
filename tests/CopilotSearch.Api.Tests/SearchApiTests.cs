using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using CopilotSearch.Contracts;
using CopilotSearch.Llm;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CopilotSearch.Api.Tests;

public sealed class ScriptedClient(Func<int, IList<ChatMessage>, ChatResponse> script) : IChatClient
{
    private int _calls;

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(script(_calls++, messages.ToList()));

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var u in (await GetResponseAsync(messages, options, cancellationToken)).ToChatResponseUpdates())
            yield return u;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;
    public void Dispose() { }
}

file sealed class FixedFactory(Func<IChatClient> client) : IChatClientFactory
{
    public LlmClient Create(string? providerOverride = null) => new(client(), providerOverride ?? "AzureOpenAI", "fake");
}

/// <summary>Real tools + mock connectors; only the LLM is scripted.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public static ChatResponse Call(string id, string tool, Dictionary<string, object?> args) =>
        new(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent(id, tool, args)]));

    /// <summary>First turn calls search_emails; second cites the first mail id found in the tool result.</summary>
    public static IChatClient EmailThenCite() => new ScriptedClient((i, messages) =>
    {
        if (i == 0)
            return Call("c1", ToolNames.SearchEmails, new() { ["query"] = "Q3 budget" });

        var result = messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().First().Result!.ToString()!;
        var id = Regex.Match(result, "\"id\":\"(mail:[^\"]+)\"").Groups[1].Value;
        return new ChatResponse(new ChatMessage(ChatRole.Assistant, $"The Q3 budget draft is in [{id}]."));
    });

    public IChatClient Client { get; set; } = EmailThenCite();

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureServices(s =>
        {
            s.RemoveAll<IChatClientFactory>();
            s.AddSingleton<IChatClientFactory>(_ => new FixedFactory(() => Client));
        });
}

public class SearchApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public SearchApiTests(ApiFactory factory) => (_factory = factory).Client = ApiFactory.EmailThenCite();

    [Fact]
    public async Task Health_ReturnsOk()
    {
        var response = await _factory.CreateClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Config_ListsTheTenTools_WithoutSecrets()
    {
        var body = await _factory.CreateClient().GetStringAsync("/config");
        using var doc = JsonDocument.Parse(body);
        Assert.Equal(10, doc.RootElement.GetProperty("tools").GetArrayLength());
        Assert.DoesNotContain("api_key", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NonStreaming_ReturnsAnswerCitationsResultsAndTrace()
    {
        _factory.Client = ApiFactory.EmailThenCite();
        var response = await _factory.CreateClient().PostAsJsonAsync(
            "/search?stream=false", new { query = "Q3 budget draft", options = new { include_trace = true } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Contains("[1]", root.GetProperty("answer").GetString());
        var citation = root.GetProperty("citations")[0];
        Assert.StartsWith("mail:", citation.GetProperty("id").GetString());
        Assert.True(root.GetProperty("results").GetArrayLength() > 0);
        var trace = root.GetProperty("trace")[0];
        Assert.Equal("search_emails", trace.GetProperty("tool").GetString());
        Assert.True(trace.GetProperty("ok").GetBoolean());
    }

    [Fact]
    public async Task Streaming_EmitsSseEventsInOrder()
    {
        _factory.Client = ApiFactory.EmailThenCite();
        var response = await _factory.CreateClient().PostAsJsonAsync("/search", new { query = "Q3 budget draft" });

        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        var names = Regex.Matches(body, @"^event: (\w+)$", RegexOptions.Multiline).Select(m => m.Groups[1].Value).Distinct().ToArray();
        Assert.Equal(["run_started", "tool_call", "tool_result", "answer_delta", "results", "final"], names);
        Assert.Contains("\"run_id\"", body);
        Assert.Contains("\"duration_ms\"", body);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyQuery_Returns400(string query)
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/search", new { query });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task OverlongQuery_Returns400()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/search", new { query = new string('a', 2001) });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task LlmFailure_NonStreaming_Returns502()
    {
        _factory.Client = new ScriptedClient((_, _) => throw new InvalidOperationException("boom"));
        var response = await _factory.CreateClient().PostAsJsonAsync("/search?stream=false", new { query = "x" });
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Contains("llm_failure", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PromptInjectionMail_IsReturnedAsDataOnly()
    {
        string? toolResult = null;
        _factory.Client = new ScriptedClient((i, messages) =>
        {
            if (i == 0)
                return ApiFactory.Call("c1", ToolNames.SearchEmails, new() { ["query"] = "invoice support terms" });
            toolResult = messages.SelectMany(m => m.Contents).OfType<FunctionResultContent>().First().Result!.ToString();
            return new ChatResponse(new ChatMessage(ChatRole.Assistant, "There is an invoice email from a vendor."));
        });

        var response = await _factory.CreateClient().PostAsJsonAsync("/search?stream=false", new { query = "vendor invoice" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(toolResult);
        Assert.Contains("fabrikam", toolResult!, StringComparison.OrdinalIgnoreCase);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain("ignore all previous", doc.RootElement.GetProperty("answer").GetString()!, StringComparison.OrdinalIgnoreCase);
    }
}

