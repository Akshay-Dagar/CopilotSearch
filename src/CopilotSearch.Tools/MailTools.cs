using System.ComponentModel;
using CopilotSearch.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CopilotSearch.Tools;

/// <summary>search_emails and get_email.</summary>
public sealed class MailTools(
    IMailConnector mail,
    IOptions<ToolOptions> options,
    IUserContext userContext,
    ILogger<MailTools> logger) : ToolBase(options, userContext, logger)
{
    private const int DefaultEmailChars = 6000;
    private const string MailPrefix = Sources.Mail + ":";

    [Description("Searches the user's email with structured filters. At least one of query, from, to, cc, start, end, has_attachments, importance, is_unread or a folder other than 'all' is required. Returns snippets (body preview) with metadata; use get_email for the full body. Resolve people via resolve_person first and pass their email address in from/to/cc.")]
    public Task<object> SearchEmails(
        [Description("Keywords to match in subject/body.")] string? query = null,
        [Description("Sender email address. Resolve the person via resolve_person first.")] string? from = null,
        [Description("Recipient (To) email address.")] string? to = null,
        [Description("Cc recipient email address.")] string? cc = null,
        [Description("Only mail received at or after this ISO-8601 UTC date-time, e.g. 2026-11-05T00:00:00Z. A date-only value (2026-11-05) means the start of that day in the user's time zone.")] string? start = null,
        [Description("Only mail received at or before this ISO-8601 UTC date-time. A date-only value (2026-11-05) is inclusive: it means the end of that day in the user's time zone.")] string? end = null,
        [Description("true = only mail with attachments, false = only mail without.")] bool? has_attachments = null,
        [Description("Mail folder: 'inbox', 'sent', 'drafts', 'archive' or 'all'. Default 'all'.")] string? folder = null,
        [Description("Importance filter: 'low', 'normal' or 'high'.")] string? importance = null,
        [Description("true = only unread mail, false = only read mail.")] bool? is_unread = null,
        [Description("Sort order: 'relevance', 'date_desc' or 'date_asc'. Default 'relevance'.")] string? sort = null,
        [Description("Maximum results. Default 10, maximum 25.")] int? top = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(ToolNames.SearchEmails, cancellationToken, async ct =>
        {
            var folderValue = ParseEnum(folder, "folder", "all", "inbox", "sent", "drafts", "archive", "all");
            var importanceValue = ParseEnum(importance, "importance", "", "low", "normal", "high");
            var sortValue = ParseEnum(sort, "sort", "relevance", "relevance", "date_desc", "date_asc");
            var range = ParseRange(start, end);

            var request = new MailSearchRequest
            {
                Query = Blank(query),
                From = Blank(from),
                To = Blank(to),
                Cc = Blank(cc),
                Range = range,
                HasAttachments = has_attachments,
                Folder = folderValue,
                Importance = importanceValue.Length == 0 ? null : importanceValue,
                IsUnread = is_unread,
                Sort = sortValue,
                Top = ClampTop(top),
            };

            var hasFilter = request.Query is not null || request.From is not null || request.To is not null
                || request.Cc is not null || range.Start is not null || range.End is not null
                || request.HasAttachments is not null || request.Importance is not null
                || request.IsUnread is not null || folderValue != "all";
            if (!hasFilter)
                throw new ToolArgumentException("Provide at least one of: query, from, to, cc, start, end, has_attachments, folder, importance, is_unread.");

            var page = await mail.SearchAsync(request, ct);
            var mapper = new ResultMapper(Options.MaxSnippetChars);
            var items = page.Items.Select(h => mapper.Mail(h.Message, h.Score)).ToList();

            return new SearchEnvelope
            {
                Results = items,
                Returned = items.Count,
                TotalEstimate = Math.Max(page.Total, items.Count),
                Truncated = page.Total > items.Count,
                QueryUsed = DescribeQuery(("query", request.Query), ("from", request.From), ("to", request.To),
                    ("cc", request.Cc), ("start", range.Start), ("end", range.End),
                    ("has_attachments", request.HasAttachments), ("folder", folderValue),
                    ("importance", request.Importance), ("is_unread", request.IsUnread), ("sort", sortValue)),
            };
        });

    [Description("Fetches one email in full: subject, sender, recipients, plain-text body (truncated to max_chars), attachments and optionally the rest of its conversation thread. Use an id returned by search_emails or search_all.")]
    public Task<object> GetEmail(
        [Description("Message id from a search result; either the native id or the 'mail:'-prefixed id.")] string message_id,
        [Description("true to also return the other messages in the conversation thread. Default false.")] bool? include_thread = null,
        [Description("Maximum body characters to return. Default 6000.")] int? max_chars = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(ToolNames.GetEmail, cancellationToken, async ct =>
        {
            if (string.IsNullOrWhiteSpace(message_id))
                throw new ToolArgumentException("'message_id' is required.");

            var id = message_id.Trim();
            if (id.StartsWith(MailPrefix, StringComparison.OrdinalIgnoreCase)) id = id[MailPrefix.Length..];
            var limit = ClampChars(max_chars, DefaultEmailChars);

            var message = await mail.GetAsync(id, ct)
                ?? throw new ToolNotFoundException($"No email found with id '{message_id}'.");

            IReadOnlyList<EmailThreadItem>? thread = null;
            if (include_thread == true && !string.IsNullOrEmpty(message.ConversationId))
            {
                var mapper = new ResultMapper(Options.MaxSnippetChars);
                var related = await mail.ListThreadAsync(message.ConversationId, ct);
                thread = related
                    .Where(m => m.Id != message.Id)
                    .OrderBy(m => m.SentDateTime)
                    .Select(m => new EmailThreadItem($"{MailPrefix}{m.Id}", ResultMapper.Format(m.From), m.SentDateTime,
                        mapper.Snippet(m.BodyPreview.Length > 0 ? m.BodyPreview : m.BodyText)))
                    .ToList();
            }

            var body = message.BodyText;
            return new EmailResult
            {
                Id = $"{MailPrefix}{message.Id}",
                Subject = message.Subject,
                From = ResultMapper.Format(message.From),
                To = message.ToRecipients.Select(ResultMapper.Format).ToList(),
                Cc = message.CcRecipients.Select(ResultMapper.Format).ToList(),
                SentAt = message.SentDateTime,
                BodyText = body.Length <= limit ? body : body[..limit],
                Attachments = message.Attachments
                    .Select(a => new EmailAttachmentInfo(a.Name, a.Size, a.ContentType,
                        a.DriveId is null || a.ItemId is null ? null : $"{a.DriveId}:{a.ItemId}"))
                    .ToList(),
                Thread = thread,
                Truncated = body.Length > limit,
            };
        });
}
