using CopilotSearch.Contracts;
using Microsoft.Extensions.Options;

namespace CopilotSearch.Connectors;

public sealed class MockCopilotChatConnector(FixtureStore fixtures, IOptions<MockOptions> options) : ICopilotChatConnector
{
    private readonly MockOptions _options = options.Value;

    public async Task<SearchPage<AiInteractionHit>> SearchAsync(CopilotChatQuery query, CancellationToken ct)
    {
        await MockSearch.SimulateLatencyAsync(_options, MockConnectorNames.CopilotChat, ct).ConfigureAwait(false);

        var candidates = fixtures.Interactions.Where(i =>
            MockSearch.InRange(i.CreatedDateTime, query.Range)
            && (MockSearch.IsAny(query.App) || string.Equals(i.AppClass, query.App, StringComparison.OrdinalIgnoreCase)));

        var ranked = MockSearch.Rank(candidates, query.Query, i => new DocText("", i.Body), i => i.CreatedDateTime, _options.ReferenceTime);
        var items = ranked.Take(MockSearch.NormalizeTop(query.Top)).Select(r => new AiInteractionHit(r.Item, r.Score)).ToList();
        return new SearchPage<AiInteractionHit>(items, ranked.Count);
    }
}
