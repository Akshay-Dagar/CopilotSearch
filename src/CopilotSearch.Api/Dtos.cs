using System.Text.Json.Serialization;
using CopilotSearch.Contracts;
using CopilotSearch.Orchestration;

namespace CopilotSearch.Api;

public sealed record SearchRequestDto
{
    [JsonPropertyName("query")] public string? Query { get; init; }
    /// <summary>AzureOpenAI | Claude. Overrides the configured provider for this request.</summary>
    [JsonPropertyName("provider")] public string? Provider { get; init; }
    [JsonPropertyName("options")] public SearchOptionsDto? Options { get; init; }
}

public sealed record SearchOptionsDto
{
    [JsonPropertyName("max_results")] public int? MaxResults { get; init; }
    /// <summary>Non-stream mode only: include tool call trace in the response.</summary>
    [JsonPropertyName("include_trace")] public bool? IncludeTrace { get; init; }
}

public sealed record ToolTraceDto(
    [property: JsonPropertyName("tool")] string Tool,
    [property: JsonPropertyName("arguments")] IReadOnlyDictionary<string, object?> Arguments,
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("duration_ms")] long DurationMs,
    [property: JsonPropertyName("returned")] int? Returned,
    [property: JsonPropertyName("error_code")] string? ErrorCode);

/// <summary>Non-streaming response: the content of the final event plus the ranked results.</summary>
public sealed record SearchResponseDto
{
    [JsonPropertyName("run_id")] public required string RunId { get; init; }
    [JsonPropertyName("provider")] public required string Provider { get; init; }
    [JsonPropertyName("model")] public required string Model { get; init; }
    [JsonPropertyName("answer")] public required string Answer { get; init; }
    [JsonPropertyName("citations")] public required IReadOnlyList<CitationInfo> Citations { get; init; }
    [JsonPropertyName("results")] public required IReadOnlyList<ResultItem> Results { get; init; }
    [JsonPropertyName("usage")] public required UsageInfo Usage { get; init; }
    [JsonPropertyName("duration_ms")] public required long DurationMs { get; init; }
    [JsonPropertyName("trace")] public IReadOnlyList<ToolTraceDto>? Trace { get; init; }
}

public sealed record ApiError(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message);
