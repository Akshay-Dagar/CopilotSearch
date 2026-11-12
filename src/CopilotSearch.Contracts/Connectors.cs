namespace CopilotSearch.Contracts;

// Requests carry all filters. Connectors apply them (mock: in memory; Graph: KQL/$filter).
// Connector methods throw on upstream failure; tools translate to ErrorEnvelope.

public sealed record DateRange(DateTimeOffset? Start, DateTimeOffset? End);

// ---- Cross-source search (Graph POST /search/query) ----
public static class SearchEntityTypes
{
    public const string Message = "message";
    public const string DriveItem = "driveItem";
    public const string ChatMessage = "chatMessage";
    public const string Event = "event";
}

public sealed record SearchRequest(string Query, IReadOnlyList<string> EntityTypes, DateRange Range, int Top);

/// <summary>Exactly one of the typed payloads is set, matching EntityType.</summary>
public sealed record SearchHit
{
    public required string EntityType { get; init; }
    public double Rank { get; init; }
    public string Summary { get; init; } = "";
    public GraphMessage? Message { get; init; }
    public GraphChatMessage? ChatMessage { get; init; }
    public GraphDriveItem? DriveItem { get; init; }
    public GraphEvent? Event { get; init; }
}

public sealed record SearchResponse(IReadOnlyList<SearchHit> Hits, int Total);

public interface ISearchConnector
{
    Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken ct);
}

// ---- People ----
public interface IPeopleConnector
{
    Task<UserProfile> GetMeAsync(CancellationToken ct);
    Task<IReadOnlyList<Person>> ResolveAsync(string name, int top, CancellationToken ct);
}

// ---- Mail ----
public sealed record MailSearchRequest
{
    public string? Query { get; init; }
    public string? From { get; init; }
    public string? To { get; init; }
    public string? Cc { get; init; }
    public DateRange Range { get; init; } = new(null, null);
    public bool? HasAttachments { get; init; }
    public string Folder { get; init; } = "all"; // inbox|sent|drafts|archive|all
    public string? Importance { get; init; }
    public bool? IsUnread { get; init; }
    public string Sort { get; init; } = "relevance"; // relevance|date_desc|date_asc
    public int Top { get; init; } = 10;
}

/// <summary>Search hit with the connector's relevance score (higher is better).</summary>
public sealed record MailHit(GraphMessage Message, double Score);

public interface IMailConnector
{
    Task<SearchPage<MailHit>> SearchAsync(MailSearchRequest request, CancellationToken ct);
    Task<GraphMessage?> GetAsync(string messageId, CancellationToken ct);
    Task<IReadOnlyList<GraphMessage>> ListThreadAsync(string conversationId, CancellationToken ct);
}

public sealed record SearchPage<T>(IReadOnlyList<T> Items, int Total);

// ---- Teams ----
public sealed record TeamsSearchRequest
{
    public string? Query { get; init; }
    public string? From { get; init; }
    public string? Mentions { get; init; }
    public string Scope { get; init; } = "any"; // chat|channel|any
    public string? Team { get; init; }
    public string? Channel { get; init; }
    public DateRange Range { get; init; } = new(null, null);
    public int Top { get; init; } = 10;
}

public sealed record TeamsHit(GraphChatMessage Message, double Score);

public interface ITeamsConnector
{
    Task<SearchPage<TeamsHit>> SearchAsync(TeamsSearchRequest request, CancellationToken ct);
}

// ---- Files ----
public sealed record FileSearchRequest
{
    public string? Query { get; init; }
    public IReadOnlyList<string>? FileTypes { get; init; }
    public string Location { get; init; } = "any"; // sharepoint|onedrive|any
    public string? Site { get; init; }
    public string? Author { get; init; }
    public string? ModifiedBy { get; init; }
    public DateRange Range { get; init; } = new(null, null);
    public int Top { get; init; } = 10;
}

public sealed record FileHit(GraphDriveItem Item, double Score);

public interface IFilesConnector
{
    Task<SearchPage<FileHit>> SearchAsync(FileSearchRequest request, CancellationToken ct);
    Task<FileContent?> GetContentAsync(string driveId, string itemId, CancellationToken ct);
}

// ---- Copilot chats ----
public sealed record CopilotChatQuery
{
    public string? Query { get; init; }
    public DateRange Range { get; init; } = new(null, null);
    public string App { get; init; } = "any"; // bizchat|teams|word|excel|powerpoint|outlook|any
    public int Top { get; init; } = 10;
}

public sealed record AiInteractionHit(AiInteraction Interaction, double Score);

public interface ICopilotChatConnector
{
    Task<SearchPage<AiInteractionHit>> SearchAsync(CopilotChatQuery query, CancellationToken ct);
}
