using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using CopilotSearch.Contracts;
using Microsoft.Extensions.AI;

namespace CopilotSearch.Orchestration;

/// <summary>
/// Wraps a tool for one run: emits tool_call/tool_result events, records result items in the
/// registry, truncates oversized output and converts unexpected failures into error envelopes.
/// </summary>
public sealed class InstrumentedFunction(
    AIFunction inner,
    ResultRegistry registry,
    ChannelWriter<SearchEvent> events,
    int maxOutputChars) : DelegatingAIFunction(inner)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var callId = FunctionInvokingChatClient.CurrentContext?.CallContent.CallId ?? Guid.NewGuid().ToString("N");
        events.TryWrite(new ToolCallEvent(callId, Name, new Dictionary<string, object?>(arguments)));

        var sw = Stopwatch.StartNew();
        JsonNode node;
        try
        {
            var raw = await base.InvokeCoreAsync(arguments, cancellationToken).ConfigureAwait(false);
            node = JsonSerializer.SerializeToNode(raw, AIJsonUtilities.DefaultOptions) ?? new JsonObject();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            node = JsonSerializer.SerializeToNode(
                ErrorEnvelope.Create(ToolErrorCodes.UpstreamError, ex.Message), JsonOptions)!;
        }

        var (ok, errorCode, returned, warnings) = Inspect(node);
        node = ToolOutputTruncator.Truncate(node, maxOutputChars);
        sw.Stop();

        events.TryWrite(new ToolResultEvent(callId, Name, ok, sw.ElapsedMilliseconds, returned, errorCode, warnings));
        return JsonSerializer.SerializeToElement(node);
    }

    private (bool Ok, string? ErrorCode, int? Returned, IReadOnlyList<string> Warnings) Inspect(JsonNode node)
    {
        if (node is not JsonObject obj)
            return (true, null, null, []);

        if (obj["error"] is JsonObject error)
            return (false, error["code"]?.GetValue<string>(), null, []);

        int? returned = null;
        if (obj["results"] is JsonArray results)
        {
            returned = results.Count;
            foreach (var entry in results)
            {
                try
                {
                    var item = entry.Deserialize<ResultItem>(JsonOptions);
                    if (item is not null)
                        registry.Add(item);
                }
                catch (JsonException)
                {
                    // Malformed items are simply not citable.
                }
            }
        }

        var warnings = obj["warnings"] is JsonArray w
            ? w.Select(x => x?.GetValue<string>() ?? "").Where(x => x.Length > 0).ToList()
            : [];
        return (true, null, returned, warnings);
    }
}


