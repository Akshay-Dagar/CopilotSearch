using CopilotSearch.Contracts;

namespace CopilotSearch.Orchestration;

public sealed record SearchRunRequest(string Query, string? Provider = null, int MaxResults = 10);

public sealed record CitationInfo(int N, string Id, string Title, string? Url, string Source);

public sealed record UsageInfo(long InputTokens, long OutputTokens);

/// <summary>Events streamed to the client. <see cref="Name"/> is the SSE event name.</summary>
public abstract record SearchEvent([property: System.Text.Json.Serialization.JsonIgnore] string Name);

public sealed record RunStartedEvent(string RunId, string Provider, string Model) : SearchEvent("run_started");

public sealed record ToolCallEvent(string CallId, string Tool, IReadOnlyDictionary<string, object?> Arguments)
    : SearchEvent("tool_call");

public sealed record ToolResultEvent(
    string CallId, string Tool, bool Ok, long DurationMs, int? Returned, string? ErrorCode, IReadOnlyList<string> Warnings)
    : SearchEvent("tool_result");

public sealed record AnswerDeltaEvent(string Text) : SearchEvent("answer_delta");

public sealed record ResultsEvent(IReadOnlyList<ResultItem> Results) : SearchEvent("results");

public sealed record FinalEvent(
    string RunId, string Answer, IReadOnlyList<CitationInfo> Citations, UsageInfo Usage, long DurationMs)
    : SearchEvent("final");

public sealed record ErrorEvent(string Code, string Message) : SearchEvent("error");

public static class RunErrorCodes
{
    public const string InvalidProvider = "invalid_provider";
    public const string Configuration = "configuration_error";
    public const string BudgetExceeded = "budget_exceeded";
    public const string LlmFailure = "llm_failure";
    public const string Cancelled = "cancelled";
}

