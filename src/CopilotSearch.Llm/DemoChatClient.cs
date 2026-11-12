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
        var toolResults = list.SelectMany(m => m.Contents).OfType<FunctionResultContent>().ToList();

        if (toolResults.Count == 0)
        {
            var query = list.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? "";
            var calls = new List<AIContent> { Call("search_all", query) };
            if (query.Contains("copilot", StringComparison.OrdinalIgnoreCase))
                calls.Add(Call("search_copilot_chats", query));
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, calls)));
        }

        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Summarize(toolResults.Select(r => r.Result?.ToString())))));
    }

    private static FunctionCallContent Call(string tool, string query) =>
        new(Guid.NewGuid().ToString("N")[..8], tool,
            new Dictionary<string, object?> { ["query"] = query, ["top"] = 4 });

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

    private static string Summarize(IEnumerable<string?> toolOutputs)
    {
        var hits = new List<JsonElement>();
        string? failure = null;

        foreach (var json in toolOutputs)
        {
            if (string.IsNullOrWhiteSpace(json))
                continue;
            try
            {
                var root = JsonDocument.Parse(json).RootElement;
                if (root.TryGetProperty("error", out var error))
                    failure ??= error.GetProperty("message").GetString();
                else
                    hits.AddRange(root.GetProperty("results").EnumerateArray());
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                failure ??= "I could not read the search results.";
            }
        }

        if (hits.Count == 0)
            return failure is null
                ? "I couldn't find anything relevant in your mail, Teams, files, calendar, or Copilot chats."
                : $"The search failed: {failure}";

        var sb = new StringBuilder("Here is what I found across your work data (demo mode, keyword matching only):\n\n");
        foreach (var r in hits.OrderByDescending(h => h.TryGetProperty("score", out var s) ? s.GetDouble() : 0).Take(MaxHits))
        {
            var snippet = r.TryGetProperty("snippet", out var sn) ? sn.GetString() : "";
            snippet = snippet is { Length: > 140 } ? snippet[..140].TrimEnd() + "…" : snippet;
            sb.Append("- **").Append(r.GetProperty("title").GetString()).Append("** (")
              .Append(r.GetProperty("source").GetString()).Append("): ").Append(snippet)
              .Append(" [").Append(r.GetProperty("id").GetString()).Append("]\n");
        }
        return sb.ToString().TrimEnd();
    }
}

