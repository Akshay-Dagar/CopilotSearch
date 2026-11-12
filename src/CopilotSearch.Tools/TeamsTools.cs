using System.ComponentModel;
using CopilotSearch.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CopilotSearch.Tools;

/// <summary>search_teams_messages.</summary>
public sealed class TeamsTools(
    ITeamsConnector teams,
    IOptions<ToolOptions> options,
    IUserContext userContext,
    ILogger<TeamsTools> logger) : ToolBase(options, userContext, logger)
{
    [Description("Searches Teams chat and channel messages. Returns message snippets with chat/channel metadata; thread expansion is not available. Resolve people via resolve_person first and pass their name or email in from/mentions.")]
    public Task<object> SearchTeamsMessages(
        [Description("Keywords to match in the message text.")] string? query = null,
        [Description("Sender name or email. Resolve the person via resolve_person first.")] string? from = null,
        [Description("Name or email of a user who is @mentioned in the message.")] string? mentions = null,
        [Description("Where to search: 'chat' (1:1 and group chats), 'channel' (team channels) or 'any'. Default 'any'.")] string? scope = null,
        [Description("Team name. Only used when scope is 'channel' or 'any'.")] string? team = null,
        [Description("Channel name. Only used when scope is 'channel' or 'any'.")] string? channel = null,
        [Description("Only messages sent at or after this ISO-8601 UTC date-time, e.g. 2026-11-05T00:00:00Z. A date-only value (2026-11-05) means the start of that day in the user's time zone.")] string? start = null,
        [Description("Only messages sent at or before this ISO-8601 UTC date-time. A date-only value (2026-11-05) is inclusive: it means the end of that day in the user's time zone.")] string? end = null,
        [Description("Maximum results. Default 10, maximum 25.")] int? top = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(ToolNames.SearchTeamsMessages, cancellationToken, async ct =>
        {
            var scopeValue = ParseEnum(scope, "scope", "any", "chat", "channel", "any");
            var range = ParseRange(start, end);
            var warnings = new List<string>();

            var teamName = Blank(team);
            var channelName = Blank(channel);
            if (scopeValue == "chat" && (teamName is not null || channelName is not null))
            {
                warnings.Add("'team' and 'channel' were ignored because scope is 'chat'.");
                teamName = channelName = null;
            }

            var request = new TeamsSearchRequest
            {
                Query = Blank(query),
                From = Blank(from),
                Mentions = Blank(mentions),
                Scope = scopeValue,
                Team = teamName,
                Channel = channelName,
                Range = range,
                Top = ClampTop(top),
            };

            var page = await teams.SearchAsync(request, ct);
            var mapper = new ResultMapper(Options.MaxSnippetChars);
            var items = page.Items.Select(h => mapper.Teams(h.Message, h.Score)).ToList();

            return new SearchEnvelope
            {
                Results = items,
                Returned = items.Count,
                TotalEstimate = Math.Max(page.Total, items.Count),
                Truncated = page.Total > items.Count,
                Warnings = warnings,
                QueryUsed = DescribeQuery(("query", request.Query), ("from", request.From), ("mentions", request.Mentions),
                    ("scope", scopeValue), ("team", teamName), ("channel", channelName),
                    ("start", range.Start), ("end", range.End)),
            };
        });
}
