using CopilotSearch.Contracts;
using Microsoft.Extensions.Options;

namespace CopilotSearch.Connectors;

public sealed class MockMailConnector(FixtureStore fixtures, IOptions<MockOptions> options) : IMailConnector
{
    private readonly MockOptions _options = options.Value;

    public async Task<SearchPage<MailHit>> SearchAsync(MailSearchRequest request, CancellationToken ct)
    {
        await MockSearch.SimulateLatencyAsync(_options, MockConnectorNames.Mail, ct).ConfigureAwait(false);

        var candidates = fixtures.Messages.Where(m => Matches(m, request));
        var ranked = MockSearch.Rank(
            candidates,
            request.Query,
            m => new DocText(m.Subject, m.BodyText + " " + string.Join(' ', m.Attachments.Select(a => a.Name)), m.From.Name),
            m => m.ReceivedDateTime,
            _options.ReferenceTime);

        var ordered = request.Sort?.ToLowerInvariant() switch
        {
            "date_desc" => ranked.OrderByDescending(r => r.Item.ReceivedDateTime).ToList(),
            "date_asc" => ranked.OrderBy(r => r.Item.ReceivedDateTime).ToList(),
            _ => ranked,
        };

        var items = ordered.Take(MockSearch.NormalizeTop(request.Top)).Select(r => new MailHit(r.Item, r.Score)).ToList();
        return new SearchPage<MailHit>(items, ordered.Count);
    }

    public async Task<GraphMessage?> GetAsync(string messageId, CancellationToken ct)
    {
        await MockSearch.SimulateLatencyAsync(_options, MockConnectorNames.Mail, ct).ConfigureAwait(false);
        return fixtures.Messages.FirstOrDefault(m => string.Equals(m.Id, messageId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyList<GraphMessage>> ListThreadAsync(string conversationId, CancellationToken ct)
    {
        await MockSearch.SimulateLatencyAsync(_options, MockConnectorNames.Mail, ct).ConfigureAwait(false);
        return fixtures.Messages
            .Where(m => string.Equals(m.ConversationId, conversationId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(m => m.SentDateTime)
            .ToList();
    }

    private static bool Matches(GraphMessage m, MailSearchRequest r) =>
        MockSearch.InRange(m.ReceivedDateTime, r.Range)
        && MockSearch.MatchesPerson(m.From, r.From)
        && MockSearch.MatchesAnyPerson(m.ToRecipients, r.To)
        && MockSearch.MatchesAnyPerson(m.CcRecipients, r.Cc)
        && (r.HasAttachments is null || m.HasAttachments == r.HasAttachments)
        && (MockSearch.IsAny(r.Folder) || string.Equals(m.ParentFolder, r.Folder, StringComparison.OrdinalIgnoreCase))
        && (string.IsNullOrWhiteSpace(r.Importance) || string.Equals(m.Importance, r.Importance, StringComparison.OrdinalIgnoreCase))
        && (r.IsUnread is null || !m.IsRead == r.IsUnread);
}
