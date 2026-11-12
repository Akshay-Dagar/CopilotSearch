using CopilotSearch.Contracts;
using Microsoft.Extensions.Options;

namespace CopilotSearch.Connectors;

/// <summary>Mock of Graph /search/query across mail, files, Teams messages and calendar events.</summary>
public sealed class MockSearchConnector(FixtureStore fixtures, IOptions<MockOptions> options) : ISearchConnector
{
    private readonly MockOptions _options = options.Value;

    public async Task<SearchResponse> SearchAsync(SearchRequest request, CancellationToken ct)
    {
        await MockSearch.SimulateLatencyAsync(_options, MockConnectorNames.Search, ct).ConfigureAwait(false);

        var types = request.EntityTypes is { Count: > 0 }
            ? request.EntityTypes.Select(t => t.ToLowerInvariant()).ToHashSet()
            : [.. new[] { SearchEntityTypes.Message, SearchEntityTypes.DriveItem, SearchEntityTypes.ChatMessage, SearchEntityTypes.Event }.Select(t => t.ToLowerInvariant())];
        var range = request.Range ?? new DateRange(null, null);
        var now = _options.ReferenceTime;
        var hits = new List<SearchHit>();

        if (types.Contains(SearchEntityTypes.Message.ToLowerInvariant()))
        {
            hits.AddRange(MockSearch.Rank(
                    fixtures.Messages.Where(m => MockSearch.InRange(m.ReceivedDateTime, range)),
                    request.Query,
                    m => new DocText(m.Subject, m.BodyText, m.From.Name),
                    m => m.ReceivedDateTime, now)
                .Select(r => new SearchHit
                {
                    EntityType = SearchEntityTypes.Message,
                    Rank = r.Score,
                    Summary = MockSearch.Truncate(r.Item.BodyPreview, 200),
                    Message = r.Item,
                }));
        }

        if (types.Contains(SearchEntityTypes.DriveItem.ToLowerInvariant()))
        {
            hits.AddRange(MockSearch.Rank(
                    fixtures.DriveItems.Where(i => MockSearch.InRange(i.LastModifiedDateTime, range)),
                    request.Query,
                    i => new DocText(i.Name, i.Summary + " " + FileText(i), i.CreatedBy?.Name ?? ""),
                    i => i.LastModifiedDateTime, now)
                .Select(r => new SearchHit
                {
                    EntityType = SearchEntityTypes.DriveItem,
                    Rank = r.Score,
                    Summary = MockSearch.Truncate(r.Item.Summary, 200),
                    DriveItem = r.Item,
                }));
        }

        if (types.Contains(SearchEntityTypes.ChatMessage.ToLowerInvariant()))
        {
            hits.AddRange(MockSearch.Rank(
                    fixtures.ChatMessages.Where(m => MockSearch.InRange(m.CreatedDateTime, range)),
                    request.Query,
                    m => new DocText($"{m.ChannelName} {m.TeamName} {m.ChatTopic}", m.Body, m.From.Name),
                    m => m.CreatedDateTime, now)
                .Select(r => new SearchHit
                {
                    EntityType = SearchEntityTypes.ChatMessage,
                    Rank = r.Score,
                    Summary = MockSearch.Truncate(r.Item.Body, 200),
                    ChatMessage = r.Item,
                }));
        }

        if (types.Contains(SearchEntityTypes.Event.ToLowerInvariant()))
        {
            hits.AddRange(MockSearch.Rank(
                    fixtures.Events.Where(e => MockSearch.InRange(e.Start, range)),
                    request.Query,
                    e => new DocText(e.Subject, e.BodyPreview, e.Organizer?.Name ?? ""),
                    e => e.Start, now)
                .Select(r => new SearchHit
                {
                    EntityType = SearchEntityTypes.Event,
                    Rank = r.Score,
                    Summary = MockSearch.Truncate(r.Item.BodyPreview, 200),
                    Event = r.Item,
                }));
        }

        var ordered = hits.OrderByDescending(h => h.Rank).ThenByDescending(HitDate).ToList();
        var top = request.Top <= 0 ? 10 : request.Top;
        return new SearchResponse(ordered.Take(top).ToList(), ordered.Count);
    }

    private string FileText(GraphDriveItem item) =>
        fixtures.FileTexts.TryGetValue(FixtureStore.FileKey(item.DriveId, item.Id), out var text) ? text : "";

    private static DateTimeOffset HitDate(SearchHit hit) =>
        hit.Message?.ReceivedDateTime ?? hit.DriveItem?.LastModifiedDateTime
        ?? hit.ChatMessage?.CreatedDateTime ?? hit.Event?.Start ?? DateTimeOffset.MinValue;
}
