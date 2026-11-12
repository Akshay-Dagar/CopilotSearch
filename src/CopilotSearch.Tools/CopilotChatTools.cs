using System.ComponentModel;
using CopilotSearch.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CopilotSearch.Tools;

/// <summary>search_copilot_chats.</summary>
public sealed class CopilotChatTools(
    ICopilotChatConnector chats,
    IOptions<ToolOptions> options,
    ILogger<CopilotChatTools> logger) : ToolBase(options, logger)
{
    [Description("Searches the user's own past Microsoft 365 Copilot conversations (prompts and responses). Use it for 'what did I ask Copilot about...' questions. Slower than the other search tools.")]
    public Task<object> SearchCopilotChats(
        [Description("Keywords to match in the prompt or response text.")] string? query = null,
        [Description("Only interactions at or after this ISO-8601 UTC date-time, e.g. 2026-11-05T00:00:00Z.")] string? start = null,
        [Description("Only interactions at or before this ISO-8601 UTC date-time.")] string? end = null,
        [Description("Copilot app: 'bizchat', 'teams', 'word', 'excel', 'powerpoint', 'outlook' or 'any'. Default 'any'.")] string? app = null,
        [Description("Maximum results. Default 10, maximum 25.")] int? top = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(ToolNames.SearchCopilotChats, cancellationToken, async ct =>
        {
            var appValue = ParseEnum(app, "app", "any", "bizchat", "teams", "word", "excel", "powerpoint", "outlook", "any");
            var range = ParseRange(start, end);
            var request = new CopilotChatQuery { Query = Blank(query), Range = range, App = appValue, Top = ClampTop(top) };

            var page = await chats.SearchAsync(request, ct);
            var mapper = new ResultMapper(Options.MaxSnippetChars);
            var items = page.Items.Select(h => mapper.CopilotChat(h.Interaction, h.Score)).ToList();

            return new SearchEnvelope
            {
                Results = items,
                Returned = items.Count,
                TotalEstimate = Math.Max(page.Total, items.Count),
                Truncated = page.Total > items.Count,
                QueryUsed = DescribeQuery(("query", request.Query), ("app", appValue),
                    ("start", range.Start), ("end", range.End)),
            };
        });
}
