using System.Text.Json.Serialization;

namespace CopilotSearch.Tools;

public sealed record ManagerInfo(
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("email")] string Email);

public sealed record CurrentUserResult
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("display_name")] public required string DisplayName { get; init; }
    [JsonPropertyName("email")] public required string Email { get; init; }
    [JsonPropertyName("upn")] public required string Upn { get; init; }
    [JsonPropertyName("job_title")] public string? JobTitle { get; init; }
    [JsonPropertyName("department")] public string? Department { get; init; }
    [JsonPropertyName("manager")] public ManagerInfo? Manager { get; init; }
    [JsonPropertyName("time_zone")] public required string TimeZone { get; init; }
    [JsonPropertyName("locale")] public required string Locale { get; init; }
}

public sealed record CurrentDatetimeResult
{
    [JsonPropertyName("utc_now")] public required DateTimeOffset UtcNow { get; init; }
    [JsonPropertyName("local_now")] public required DateTimeOffset LocalNow { get; init; }
    [JsonPropertyName("timezone")] public required string Timezone { get; init; }
    [JsonPropertyName("day_of_week")] public required string DayOfWeek { get; init; }
    /// <summary>Monday 00:00:00 of the current local week.</summary>
    [JsonPropertyName("week_start")] public required DateTimeOffset WeekStart { get; init; }
    /// <summary>Sunday 23:59:59 of the current local week.</summary>
    [JsonPropertyName("week_end")] public required DateTimeOffset WeekEnd { get; init; }
}

public sealed record PersonInfo
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("display_name")] public required string DisplayName { get; init; }
    [JsonPropertyName("email")] public required string Email { get; init; }
    [JsonPropertyName("upn")] public required string Upn { get; init; }
    [JsonPropertyName("job_title")] public string? JobTitle { get; init; }
    [JsonPropertyName("department")] public string? Department { get; init; }
    [JsonPropertyName("score")] public double Score { get; init; }
}

public sealed record ResolvePersonResult
{
    [JsonPropertyName("people")] public IReadOnlyList<PersonInfo> People { get; init; } = [];
}

public sealed record EmailAttachmentInfo(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("content_type")] string ContentType,
    [property: JsonPropertyName("file_id")] string? FileId);

public sealed record EmailThreadItem(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("from")] string From,
    [property: JsonPropertyName("sent_at")] DateTimeOffset SentAt,
    [property: JsonPropertyName("snippet")] string Snippet);

public sealed record EmailResult
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("subject")] public required string Subject { get; init; }
    [JsonPropertyName("from")] public required string From { get; init; }
    [JsonPropertyName("to")] public IReadOnlyList<string> To { get; init; } = [];
    [JsonPropertyName("cc")] public IReadOnlyList<string> Cc { get; init; } = [];
    [JsonPropertyName("sent_at")] public DateTimeOffset SentAt { get; init; }
    [JsonPropertyName("body_text")] public string BodyText { get; init; } = "";
    [JsonPropertyName("attachments")] public IReadOnlyList<EmailAttachmentInfo> Attachments { get; init; } = [];
    [JsonPropertyName("thread")] public IReadOnlyList<EmailThreadItem>? Thread { get; init; }
    [JsonPropertyName("truncated")] public bool Truncated { get; init; }
}

public sealed record FileContentResult
{
    [JsonPropertyName("id")] public required string Id { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("file_type")] public string FileType { get; init; } = "";
    [JsonPropertyName("text")] public string Text { get; init; } = "";
    [JsonPropertyName("extracted_chars")] public int ExtractedChars { get; init; }
    [JsonPropertyName("total_chars")] public int TotalChars { get; init; }
    [JsonPropertyName("truncated")] public bool Truncated { get; init; }
    [JsonPropertyName("web_url")] public string? WebUrl { get; init; }
}
