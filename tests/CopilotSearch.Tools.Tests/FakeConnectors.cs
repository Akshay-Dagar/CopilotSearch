using CopilotSearch.Contracts;

namespace CopilotSearch.Tools.Tests;

/// <summary>One in-memory object implementing every connector interface; tests tweak the public hooks.</summary>
public sealed class FakeConnectors : IPeopleConnector, ISearchConnector, IMailConnector, ITeamsConnector, IFilesConnector, ICopilotChatConnector
{
    public static readonly DateTimeOffset T0 = new(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);

    public TimeSpan Delay { get; set; }
    public Exception? Throw { get; set; }

    public UserProfile Me { get; set; } = new()
    {
        Id = "u-akshay", DisplayName = "Akshay Dagar", Mail = "akshay@contoso.com", UserPrincipalName = "akshay@contoso.com",
        JobTitle = "PM", Department = "Eng", Manager = new GraphEmailAddress("Maya Rao", "maya@contoso.com"), TimeZone = "Asia/Kolkata",
    };
    public List<Person> People { get; } = [];
    public List<MailHit> MailHits { get; } = [];
    public int MailTotal { get; set; } = -1;
    public Dictionary<string, GraphMessage> Messages { get; } = new();
    public List<GraphMessage> Thread { get; } = [];
    public List<TeamsHit> TeamsHits { get; } = [];
    public List<FileHit> FileHits { get; } = [];
    public Dictionary<(string, string), FileContent> FileContents { get; } = new();
    public List<AiInteractionHit> ChatHits { get; } = [];
    public SearchResponse SearchResult { get; set; } = new([], 0);

    public SearchRequest? LastSearch { get; private set; }
    public MailSearchRequest? LastMail { get; private set; }
    public TeamsSearchRequest? LastTeams { get; private set; }
    public FileSearchRequest? LastFiles { get; private set; }
    public CopilotChatQuery? LastChats { get; private set; }
    public int? LastResolveTop { get; private set; }
    public string? LastMessageId { get; private set; }

    private async Task Gate(CancellationToken ct)
    {
        if (Delay > TimeSpan.Zero) await Task.Delay(Delay, ct);
        if (Throw is not null) throw Throw;
    }

    public async Task<UserProfile> GetMeAsync(CancellationToken ct) { await Gate(ct); return Me; }

    public async Task<IReadOnlyList<Person>> ResolveAsync(string name, int top, CancellationToken ct)
    {
        await Gate(ct);
        LastResolveTop = top;
        return People.Where(p => p.DisplayName.Contains(name, StringComparison.OrdinalIgnoreCase)).Take(top).ToList();
    }

    public async Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken ct)
    {
        await Gate(ct);
        LastSearch = request;
        return SearchResult;
    }

    public async Task<SearchPage<MailHit>> SearchAsync(MailSearchRequest request, CancellationToken ct)
    {
        await Gate(ct);
        LastMail = request;
        var items = MailHits.Take(request.Top).ToList();
        return new(items, MailTotal >= 0 ? MailTotal : MailHits.Count);
    }

    public async Task<GraphMessage?> GetAsync(string messageId, CancellationToken ct)
    {
        await Gate(ct);
        LastMessageId = messageId;
        return Messages.GetValueOrDefault(messageId);
    }

    public async Task<IReadOnlyList<GraphMessage>> ListThreadAsync(string conversationId, CancellationToken ct)
    {
        await Gate(ct);
        return Thread.Where(m => m.ConversationId == conversationId).ToList();
    }

    public async Task<SearchPage<TeamsHit>> SearchAsync(TeamsSearchRequest request, CancellationToken ct)
    {
        await Gate(ct);
        LastTeams = request;
        return new(TeamsHits.Take(request.Top).ToList(), TeamsHits.Count);
    }

    public async Task<SearchPage<FileHit>> SearchAsync(FileSearchRequest request, CancellationToken ct)
    {
        await Gate(ct);
        LastFiles = request;
        return new(FileHits.Take(request.Top).ToList(), FileHits.Count);
    }

    public async Task<FileContent?> GetContentAsync(string driveId, string itemId, CancellationToken ct)
    {
        await Gate(ct);
        return FileContents.GetValueOrDefault((driveId, itemId));
    }

    public async Task<SearchPage<AiInteractionHit>> SearchAsync(CopilotChatQuery query, CancellationToken ct)
    {
        await Gate(ct);
        LastChats = query;
        return new(ChatHits.Take(query.Top).ToList(), ChatHits.Count);
    }

    public static GraphMessage Msg(string id, string subject = "Q3 budget", string? conv = "c1", string body = "Full body text") => new()
    {
        Id = id, Subject = subject, ConversationId = conv, BodyPreview = "Preview of " + subject, BodyText = body,
        From = new GraphEmailAddress("Priya", "priya@contoso.com"),
        ToRecipients = [new GraphEmailAddress("Akshay Dagar", "akshay@contoso.com")],
        CcRecipients = [new GraphEmailAddress("", "cc@contoso.com")],
        SentDateTime = T0, ReceivedDateTime = T0.AddMinutes(1), HasAttachments = true,
        Attachments = [new GraphAttachment("budget.xlsx", 1234, "application/xlsx", "d1", "i1")],
        WebLink = "https://outlook/" + id,
    };

    public static GraphChatMessage Chat(string id, bool channel = false) => new()
    {
        Id = id, ChatId = channel ? null : "chat1", TeamId = channel ? "t1" : null, TeamName = channel ? "Launch" : null,
        ChannelId = channel ? "ch1" : null, ChannelName = channel ? "General" : null, ChatTopic = channel ? null : "Budget chat",
        From = new GraphEmailAddress("Priya", "priya@contoso.com"), Body = "hello team", ReplyCount = 2,
        Mentions = [new GraphEmailAddress("Akshay", "akshay@contoso.com")], CreatedDateTime = T0, WebUrl = "https://teams/" + id,
    };

    public static GraphDriveItem Drive(string id, string name = "Budget.xlsx") => new()
    {
        DriveId = "d1", Id = id, Name = name, FileType = "xlsx", Size = 99, SiteName = "Finance", Path = "/docs",
        CreatedBy = new GraphEmailAddress("Priya", "priya@contoso.com"),
        LastModifiedBy = new GraphEmailAddress("Raj", "raj@contoso.com"),
        CreatedDateTime = T0, LastModifiedDateTime = T0.AddDays(1), WebUrl = "https://sp/" + id, Summary = "Budget summary",
    };

    public static GraphEvent Event(string id) => new()
    {
        Id = id, Subject = "Budget review", BodyPreview = "Review the budget", Organizer = new GraphEmailAddress("Maya", "maya@contoso.com"),
        Start = T0, End = T0.AddHours(1), IsOnlineMeeting = true, WebLink = "https://cal/" + id,
    };
}
