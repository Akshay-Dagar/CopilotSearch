using System.Text.Json.Serialization;

namespace CopilotSearch.Contracts;

/// <summary>Normalized search result shared by every search tool (design doc 6.1).</summary>
public sealed record ResultItem
{
    /// <summary>"&lt;source&gt;:&lt;native id&gt;", globally unique, e.g. "mail:AAMk1".</summary>
    [JsonPropertyName("id")] public required string Id { get; init; }
    /// <summary>mail | teams | files | copilot_chat | calendar</summary>
    [JsonPropertyName("source")] public required string Source { get; init; }
    /// <summary>email | chat_message | channel_message | document | copilot_interaction | event</summary>
    [JsonPropertyName("type")] public required string Type { get; init; }
    [JsonPropertyName("title")] public required string Title { get; init; }
    /// <summary>Matched text, at most 300 chars.</summary>
    [JsonPropertyName("snippet")] public string Snippet { get; init; } = "";
    [JsonPropertyName("url")] public string? Url { get; init; }
    [JsonPropertyName("author")] public ResultAuthor? Author { get; init; }
    [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; init; }
    [JsonPropertyName("modified_at")] public DateTimeOffset? ModifiedAt { get; init; }
    [JsonPropertyName("score")] public double Score { get; init; }
    [JsonPropertyName("metadata")] public Dictionary<string, object?> Metadata { get; init; } = new();
}

public sealed record ResultAuthor(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("email")] string? Email);

/// <summary>Search tool return value.</summary>
public sealed record SearchEnvelope
{
    [JsonPropertyName("results")] public IReadOnlyList<ResultItem> Results { get; init; } = [];
    [JsonPropertyName("total_estimate")] public int TotalEstimate { get; init; }
    [JsonPropertyName("returned")] public int Returned { get; init; }
    [JsonPropertyName("truncated")] public bool Truncated { get; init; }
    [JsonPropertyName("warnings")] public IReadOnlyList<string> Warnings { get; init; } = [];
    [JsonPropertyName("query_used")] public string? QueryUsed { get; init; }
}

public static class ToolErrorCodes
{
    public const string Timeout = "timeout";
    public const string NotFound = "not_found";
    public const string InvalidArgument = "invalid_argument";
    public const string UpstreamError = "upstream_error";
}

/// <summary>Returned to the LLM instead of throwing.</summary>
public sealed record ErrorEnvelope
{
    [JsonPropertyName("error")] public required ToolError Error { get; init; }

    public static ErrorEnvelope Create(string code, string message, bool retryable = false) =>
        new() { Error = new ToolError(code, message, retryable) };
}

public sealed record ToolError(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("retryable")] bool Retryable);

/// <summary>Constants shared by tools and orchestration.</summary>
public static class Sources
{
    public const string Mail = "mail";
    public const string Teams = "teams";
    public const string Files = "files";
    public const string CopilotChat = "copilot_chat";
    public const string Calendar = "calendar";
}
