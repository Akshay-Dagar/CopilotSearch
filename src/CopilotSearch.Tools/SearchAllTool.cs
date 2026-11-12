using System.ComponentModel;
using CopilotSearch.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CopilotSearch.Tools;

/// <summary>search_all: cross-source first pass.</summary>
public sealed class SearchAllTool(
    ISearchConnector search,
    IOptions<ToolOptions> options,
    ILogger<SearchAllTool> logger) : ToolBase(options, logger)
{
    private static readonly Dictionary<string, string> EntityBySource = new()
    {
        ["mail"] = SearchEntityTypes.Message,
        ["files"] = SearchEntityTypes.DriveItem,
        ["teams"] = SearchEntityTypes.ChatMessage,
        ["events"] = SearchEntityTypes.Event,
    };

    [Description("Cross-source first-pass search over email, files, Teams messages and calendar events. Returns shallow results (snippets only) merged and sorted by relevance score. Does NOT cover Copilot chats (use search_copilot_chats). Follow up with search_emails, search_teams_messages, search_files, get_email or get_file_content for detail.")]
    public Task<object> SearchAll(
        [Description("Keywords or KQL-friendly search text. Required.")] string query,
        [Description("Subset of sources to search: 'mail', 'files', 'teams', 'events'. Defaults to all four.")] string[]? sources = null,
        [Description("Only items modified/received at or after this ISO-8601 UTC date-time, e.g. 2026-11-05T00:00:00Z.")] string? start = null,
        [Description("Only items modified/received at or before this ISO-8601 UTC date-time.")] string? end = null,
        [Description("Maximum results per source. Default 10, maximum 25.")] int? top = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(ToolNames.SearchAll, cancellationToken, async ct =>
        {
            if (string.IsNullOrWhiteSpace(query))
                throw new ToolArgumentException("'query' is required.");

            var requested = (sources is { Length: > 0 } ? sources : EntityBySource.Keys.ToArray())
                .Select(s => s?.Trim().ToLowerInvariant() ?? "")
                .Distinct()
                .ToList();
            var unknown = requested.Where(s => !EntityBySource.ContainsKey(s)).ToList();
            if (unknown.Count > 0)
                throw new ToolArgumentException($"Unknown source(s): {string.Join(", ", unknown)}. Allowed: {string.Join(", ", EntityBySource.Keys)}.");

            var range = ParseRange(start, end);
            var perSource = ClampTop(top);
            var entityTypes = requested.Select(s => EntityBySource[s]).ToList();

            var response = await search.SearchAsync(new SearchRequest(query.Trim(), entityTypes, range, perSource), ct);

            var mapper = new ResultMapper(Options.MaxSnippetChars);
            var items = response.Hits
                .Where(h => entityTypes.Contains(h.EntityType))
                .GroupBy(h => h.EntityType)
                .SelectMany(g => g.OrderByDescending(h => h.Rank).Take(perSource))
                .Select(h => Map(mapper, h))
                .OfType<ResultItem>()
                .OrderByDescending(r => r.Score)
                .ToList();

            return new SearchEnvelope
            {
                Results = items,
                Returned = items.Count,
                TotalEstimate = Math.Max(response.Total, items.Count),
                Truncated = response.Total > items.Count,
                QueryUsed = DescribeQuery(("query", query.Trim()), ("sources", string.Join(',', requested)),
                    ("start", range.Start), ("end", range.End)),
            };
        });

    private static ResultItem? Map(ResultMapper mapper, SearchHit h)
    {
        var summary = string.IsNullOrWhiteSpace(h.Summary) ? null : h.Summary;
        return h.EntityType switch
        {
            SearchEntityTypes.Message when h.Message is { } m => mapper.Mail(m, h.Rank, summary),
            SearchEntityTypes.ChatMessage when h.ChatMessage is { } c => mapper.Teams(c, h.Rank, summary),
            SearchEntityTypes.DriveItem when h.DriveItem is { } d => mapper.File(d, h.Rank, summary),
            SearchEntityTypes.Event when h.Event is { } e => mapper.Event(e, h.Rank, summary),
            _ => null,
        };
    }
}
