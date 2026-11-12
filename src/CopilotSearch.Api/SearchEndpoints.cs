using System.Text.Json;
using CopilotSearch.Orchestration;

namespace CopilotSearch.Api;

public static class SearchEndpoints
{
    public const int MaxQueryLength = 2000;

    public static readonly JsonSerializerOptions SseJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public static void MapSearchEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/search", HandleSearch);
    }

    private static async Task HandleSearch(
        HttpContext http, SearchRequestDto request, ISearchOrchestrator orchestrator, bool stream = true)
    {
        var query = request.Query?.Trim();
        if (string.IsNullOrEmpty(query))
        {
            await WriteError(http, StatusCodes.Status400BadRequest, "invalid_request", "'query' is required.");
            return;
        }
        if (query.Length > MaxQueryLength)
        {
            await WriteError(http, StatusCodes.Status400BadRequest, "invalid_request", $"'query' must be at most {MaxQueryLength} characters.");
            return;
        }

        var run = new SearchRunRequest(query, request.Provider, request.Options?.MaxResults ?? 10);
        if (stream)
            await StreamAsync(http, orchestrator.RunAsync(run, http.RequestAborted));
        else
            await RespondAsync(http, orchestrator.RunAsync(run, http.RequestAborted), request.Options?.IncludeTrace == true);
    }

    private static async Task StreamAsync(HttpContext http, IAsyncEnumerable<SearchEvent> events)
    {
        http.Response.ContentType = "text/event-stream";
        http.Response.Headers.CacheControl = "no-cache";
        http.Response.Headers["X-Accel-Buffering"] = "no";

        try
        {
            await foreach (var evt in events)
            {
                var data = JsonSerializer.Serialize(evt, evt.GetType(), SseJson);
                await http.Response.WriteAsync($"event: {evt.Name}\ndata: {data}\n\n", http.RequestAborted);
                await http.Response.Body.FlushAsync(http.RequestAborted);
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected; the orchestrator observes the same token and stops the run.
        }
    }

    private static async Task RespondAsync(HttpContext http, IAsyncEnumerable<SearchEvent> events, bool includeTrace)
    {
        RunStartedEvent? started = null;
        ResultsEvent? results = null;
        FinalEvent? final = null;
        ErrorEvent? error = null;
        var calls = new Dictionary<string, ToolCallEvent>();
        var trace = new List<ToolTraceDto>();

        await foreach (var evt in events)
        {
            switch (evt)
            {
                case RunStartedEvent e: started = e; break;
                case ToolCallEvent e: calls[e.CallId] = e; break;
                case ToolResultEvent e:
                    var args = calls.TryGetValue(e.CallId, out var call) ? call.Arguments : new Dictionary<string, object?>();
                    trace.Add(new ToolTraceDto(e.Tool, args, e.Ok, e.DurationMs, e.Returned, e.ErrorCode));
                    break;
                case ResultsEvent e: results = e; break;
                case FinalEvent e: final = e; break;
                case ErrorEvent e: error = e; break;
            }
        }

        if (final is null || started is null)
        {
            var failure = error ?? new ErrorEvent("internal_error", "The run ended without a result.");
            await WriteError(http, StatusFor(failure.Code), failure.Code, failure.Message);
            return;
        }

        await http.Response.WriteAsJsonAsync(new SearchResponseDto
        {
            RunId = final.RunId,
            Provider = started.Provider,
            Model = started.Model,
            Answer = final.Answer,
            Citations = final.Citations,
            Results = results?.Results ?? [],
            Usage = final.Usage,
            DurationMs = final.DurationMs,
            Trace = includeTrace ? trace : null,
        }, SseJson);
    }

    private static int StatusFor(string code) => code switch
    {
        RunErrorCodes.InvalidProvider => StatusCodes.Status400BadRequest,
        RunErrorCodes.Configuration => StatusCodes.Status503ServiceUnavailable,
        RunErrorCodes.BudgetExceeded => StatusCodes.Status504GatewayTimeout,
        RunErrorCodes.Cancelled => 499,
        _ => StatusCodes.Status502BadGateway,
    };

    private static async Task WriteError(HttpContext http, int status, string code, string message)
    {
        http.Response.StatusCode = status;
        await http.Response.WriteAsJsonAsync(new ApiError(code, message), SseJson);
    }
}
