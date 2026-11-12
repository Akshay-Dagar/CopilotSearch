using CopilotSearch.Contracts;

namespace CopilotSearch.Connectors;

// Graph-backed connectors are a P1 deliverable; these stubs keep the DI graph complete in Graph mode.

internal static class GraphNotAvailable
{
    public static NotImplementedException Error() => new("Graph connectors are P1");
}

public sealed class GraphSearchConnector : ISearchConnector
{
    public Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken ct) => throw GraphNotAvailable.Error();
}

public sealed class GraphPeopleConnector : IPeopleConnector
{
    public Task<UserProfile> GetMeAsync(CancellationToken ct) => throw GraphNotAvailable.Error();

    public Task<IReadOnlyList<Person>> ResolveAsync(string name, int top, CancellationToken ct) => throw GraphNotAvailable.Error();
}

public sealed class GraphMailConnector : IMailConnector
{
    public Task<SearchPage<MailHit>> SearchAsync(MailSearchRequest request, CancellationToken ct) => throw GraphNotAvailable.Error();

    public Task<GraphMessage?> GetAsync(string messageId, CancellationToken ct) => throw GraphNotAvailable.Error();

    public Task<IReadOnlyList<GraphMessage>> ListThreadAsync(string conversationId, CancellationToken ct) => throw GraphNotAvailable.Error();
}

public sealed class GraphTeamsConnector : ITeamsConnector
{
    public Task<SearchPage<TeamsHit>> SearchAsync(TeamsSearchRequest request, CancellationToken ct) => throw GraphNotAvailable.Error();
}

public sealed class GraphFilesConnector : IFilesConnector
{
    public Task<SearchPage<FileHit>> SearchAsync(FileSearchRequest request, CancellationToken ct) => throw GraphNotAvailable.Error();

    public Task<FileContent?> GetContentAsync(string driveId, string itemId, CancellationToken ct) => throw GraphNotAvailable.Error();
}

public sealed class GraphCopilotChatConnector : ICopilotChatConnector
{
    public Task<SearchPage<AiInteractionHit>> SearchAsync(CopilotChatQuery query, CancellationToken ct) => throw GraphNotAvailable.Error();
}
