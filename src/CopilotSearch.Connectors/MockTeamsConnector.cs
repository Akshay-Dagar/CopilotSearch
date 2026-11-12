using CopilotSearch.Contracts;
using Microsoft.Extensions.Options;

namespace CopilotSearch.Connectors;

public sealed class MockTeamsConnector(FixtureStore fixtures, IOptions<MockOptions> options) : ITeamsConnector
{
    private readonly MockOptions _options = options.Value;

    public async Task<SearchPage<TeamsHit>> SearchAsync(TeamsSearchRequest request, CancellationToken ct)
    {
        await MockSearch.SimulateLatencyAsync(_options, MockConnectorNames.Teams, ct).ConfigureAwait(false);

        var ranked = MockSearch.Rank(
            fixtures.ChatMessages.Where(m => Matches(m, request)),
            request.Query,
            m => new DocText($"{m.ChannelName} {m.TeamName} {m.ChatTopic}", m.Body, m.From.Name),
            m => m.CreatedDateTime,
            _options.ReferenceTime);

        var items = ranked.Take(MockSearch.NormalizeTop(request.Top)).Select(r => new TeamsHit(r.Item, r.Score)).ToList();
        return new SearchPage<TeamsHit>(items, ranked.Count);
    }

    private static bool Matches(GraphChatMessage m, TeamsSearchRequest r)
    {
        var isChannel = m.TeamId is not null;
        var scopeOk = (r.Scope?.ToLowerInvariant()) switch
        {
            "chat" => !isChannel,
            "channel" => isChannel,
            _ => true,
        };

        return scopeOk
            && MockSearch.InRange(m.CreatedDateTime, r.Range)
            && MockSearch.MatchesPerson(m.From, r.From)
            && MockSearch.MatchesAnyPerson(m.Mentions, r.Mentions)
            && NameMatches(r.Team, m.TeamName, m.TeamId)
            && NameMatches(r.Channel, m.ChannelName, m.ChannelId);
    }

    private static bool NameMatches(string? filter, string? name, string? id) =>
        string.IsNullOrWhiteSpace(filter)
        || MockSearch.ContainsIgnoreCase(name, filter.Trim())
        || string.Equals(id, filter.Trim(), StringComparison.OrdinalIgnoreCase);
}
