namespace CopilotSearch.Contracts;

// Graph-shaped DTOs. Property names mirror Microsoft Graph so real connectors can map 1:1.

public sealed record GraphEmailAddress(string Name, string Address);

public sealed record UserProfile
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Mail { get; init; }
    public required string UserPrincipalName { get; init; }
    public string? JobTitle { get; init; }
    public string? Department { get; init; }
    public GraphEmailAddress? Manager { get; init; }
    public required string TimeZone { get; init; } // IANA, e.g. "Asia/Kolkata"
    public string Locale { get; init; } = "en-US";
}

public sealed record Person
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Mail { get; init; }
    public required string UserPrincipalName { get; init; }
    public string? JobTitle { get; init; }
    public string? Department { get; init; }
    public double Score { get; init; }
}

public sealed record GraphMessage
{
    public required string Id { get; init; }
    public string? ConversationId { get; init; }
    public required string Subject { get; init; }
    public string BodyPreview { get; init; } = "";
    public string BodyText { get; init; } = "";
    public required GraphEmailAddress From { get; init; }
    public IReadOnlyList<GraphEmailAddress> ToRecipients { get; init; } = [];
    public IReadOnlyList<GraphEmailAddress> CcRecipients { get; init; } = [];
    public DateTimeOffset ReceivedDateTime { get; init; }
    public DateTimeOffset SentDateTime { get; init; }
    public bool HasAttachments { get; init; }
    public IReadOnlyList<GraphAttachment> Attachments { get; init; } = [];
    public string Importance { get; init; } = "normal"; // low | normal | high
    public bool IsRead { get; init; } = true;
    public string ParentFolder { get; init; } = "inbox"; // inbox | sent | drafts | archive
    public string? WebLink { get; init; }
}

public sealed record GraphAttachment(string Name, long Size, string ContentType, string? DriveId = null, string? ItemId = null);

public sealed record GraphChatMessage
{
    public required string Id { get; init; }
    public string? ChatId { get; init; }      // set for 1:1 / group chats
    public string? TeamId { get; init; }      // set for channel messages
    public string? TeamName { get; init; }
    public string? ChannelId { get; init; }
    public string? ChannelName { get; init; }
    public string? ChatTopic { get; init; }
    public string? ReplyToId { get; init; }
    public int ReplyCount { get; init; }
    public required GraphEmailAddress From { get; init; }
    public IReadOnlyList<GraphEmailAddress> Mentions { get; init; } = [];
    public string Body { get; init; } = "";
    public DateTimeOffset CreatedDateTime { get; init; }
    public string? WebUrl { get; init; }
}

public sealed record GraphDriveItem
{
    public required string DriveId { get; init; }
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string FileType { get; init; } = "";   // extension without dot
    public long Size { get; init; }
    public string Location { get; init; } = "sharepoint"; // sharepoint | onedrive
    public string? SiteName { get; init; }
    public string? Path { get; init; }
    public GraphEmailAddress? CreatedBy { get; init; }
    public GraphEmailAddress? LastModifiedBy { get; init; }
    public DateTimeOffset CreatedDateTime { get; init; }
    public DateTimeOffset LastModifiedDateTime { get; init; }
    public string? WebUrl { get; init; }
    public string Summary { get; init; } = "";
}

public sealed record FileContent
{
    public required string DriveId { get; init; }
    public required string ItemId { get; init; }
    public required string Name { get; init; }
    public string FileType { get; init; } = "";
    public string Text { get; init; } = "";       // full extracted text; tools truncate
    public string? WebUrl { get; init; }
}

public sealed record GraphEvent
{
    public required string Id { get; init; }
    public required string Subject { get; init; }
    public string BodyPreview { get; init; } = "";
    public GraphEmailAddress? Organizer { get; init; }
    public IReadOnlyList<GraphEmailAddress> Attendees { get; init; } = [];
    public DateTimeOffset Start { get; init; }
    public DateTimeOffset End { get; init; }
    public bool IsOnlineMeeting { get; init; }
    public string? WebLink { get; init; }
}

public sealed record AiInteraction
{
    public required string Id { get; init; }
    public required string SessionId { get; init; }  // conversation id
    public string AppClass { get; init; } = "bizchat"; // bizchat|teams|word|excel|powerpoint|outlook
    public string InteractionType { get; init; } = "userPrompt"; // userPrompt | aiResponse
    public string Body { get; init; } = "";
    public DateTimeOffset CreatedDateTime { get; init; }
    public string? WebUrl { get; init; }
}
