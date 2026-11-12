using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace CopilotSearch.Llm;

/// <summary>
/// Keyless stand-in for a real model so the POC is demoable without Azure credentials:
/// turn 1 calls search_all with the user's query, turn 2 summarizes the top hits with citations.
/// </summary>
public sealed class DemoChatClient : IChatClient
{
    private const int MaxHits = 6;

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var list = messages.ToList();
        var toolResult = list.SelectMany(m => m.Contents).OfType<FunctionResultContent>().LastOrDefault();

        if (toolResult is null)
        {
            var query = list.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? "";
            var call = new FunctionCallContent(Guid.NewGuid().ToString("N")[..8], "search_all",
                new Dictionary<string, object?> { ["query"] = query, ["top"] = 4 });
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, [call])));
        }

        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Summarize(toolResult.Result?.ToString()))));
    }

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

    private static string Summarize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return "I could not read the search results.";

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var error))
                return $"The search failed: {error.GetProperty("message").GetString()}";

            var results = doc.RootElement.GetProperty("results").EnumerateArray().Take(MaxHits).ToList();
            if (results.Count == 0)
                return "I couldn't find anything relevant in your mail, Teams, files, or calendar.";

            var sb = new StringBuilder("Here is what I found across your work data (demo mode, keyword matching only):\n\n");
            foreach (var r in results)
            {
                var snippet = r.TryGetProperty("snippet", out var s) ? s.GetString() : "";
                snippet = snippet is { Length: > 140 } ? snippet[..140].TrimEnd() + "…" : snippet;
                sb.Append("- **").Append(r.GetProperty("title").GetString()).Append("** (")
                  .Append(r.GetProperty("source").GetString()).Append("): ").Append(snippet)
                  .Append(" [").Append(r.GetProperty("id").GetString()).Append("]\n");
            }
            return sb.ToString().TrimEnd();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return "I could not read the search results.";
        }
    }
}
