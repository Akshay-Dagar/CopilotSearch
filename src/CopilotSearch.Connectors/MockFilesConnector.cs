using CopilotSearch.Contracts;
using Microsoft.Extensions.Options;

namespace CopilotSearch.Connectors;

public sealed class MockFilesConnector(FixtureStore fixtures, IOptions<MockOptions> options) : IFilesConnector
{
    private readonly MockOptions _options = options.Value;

    public async Task<SearchPage<FileHit>> SearchAsync(FileSearchRequest request, CancellationToken ct)
    {
        await MockSearch.SimulateLatencyAsync(_options, MockConnectorNames.Files, ct).ConfigureAwait(false);

        var ranked = MockSearch.Rank(
            fixtures.DriveItems.Where(i => Matches(i, request)),
            request.Query,
            i => new DocText(i.Name, i.Summary + " " + Text(i), i.CreatedBy?.Name ?? ""),
            i => i.LastModifiedDateTime,
            _options.ReferenceTime);

        var items = ranked.Take(MockSearch.NormalizeTop(request.Top)).Select(r => new FileHit(r.Item, r.Score)).ToList();
        return new SearchPage<FileHit>(items, ranked.Count);
    }

    public async Task<FileContent?> GetContentAsync(string driveId, string itemId, CancellationToken ct)
    {
        await MockSearch.SimulateLatencyAsync(_options, MockConnectorNames.Files, ct).ConfigureAwait(false);
        var item = fixtures.DriveItems.FirstOrDefault(i =>
            string.Equals(i.DriveId, driveId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(i.Id, itemId, StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            return null;
        }

        return new FileContent
        {
            DriveId = item.DriveId,
            ItemId = item.Id,
            Name = item.Name,
            FileType = item.FileType,
            Text = Text(item),
            WebUrl = item.WebUrl,
        };
    }

    private string Text(GraphDriveItem item) =>
        fixtures.FileTexts.TryGetValue(FixtureStore.FileKey(item.DriveId, item.Id), out var text) ? text : "";

    private static bool Matches(GraphDriveItem i, FileSearchRequest r) =>
        MockSearch.InRange(i.LastModifiedDateTime, r.Range)
        && (MockSearch.IsAny(r.Location) || string.Equals(i.Location, r.Location, StringComparison.OrdinalIgnoreCase))
        && (string.IsNullOrWhiteSpace(r.Site) || MockSearch.ContainsIgnoreCase(i.SiteName, r.Site.Trim()))
        && MockSearch.MatchesPerson(i.CreatedBy, r.Author)
        && MockSearch.MatchesPerson(i.LastModifiedBy, r.ModifiedBy)
        && TypeMatches(i, r.FileTypes);

    private static bool TypeMatches(GraphDriveItem item, IReadOnlyList<string>? types) =>
        types is null || types.Count == 0
        || types.Any(t => string.Equals(item.FileType, t.Trim().TrimStart('.'), StringComparison.OrdinalIgnoreCase));
}
