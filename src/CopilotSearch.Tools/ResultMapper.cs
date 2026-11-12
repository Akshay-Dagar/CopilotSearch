using CopilotSearch.Contracts;

namespace CopilotSearch.Tools;

/// <summary>Maps Graph-shaped DTOs to the normalized <see cref="ResultItem"/>.</summary>
internal sealed class ResultMapper(int maxSnippetChars)
{
    public string Snippet(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var s = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return s.Length <= maxSnippetChars ? s : s[..Math.Max(0, maxSnippetChars - 1)] + "…";
    }

    private static ResultAuthor? Author(GraphEmailAddress? a) => a is null ? null : new ResultAuthor(a.Name, a.Address);

    public static string Format(GraphEmailAddress a) => string.IsNullOrEmpty(a.Name) ? a.Address : $"{a.Name} <{a.Address}>";

    public ResultItem Mail(GraphMessage m, double score, string? snippetOverride = null) => new()
    {
        Id = $"{Sources.Mail}:{m.Id}",
        Source = Sources.Mail,
        Type = "email",
        Title = m.Subject,
        Snippet = Snippet(snippetOverride ?? (m.BodyPreview.Length > 0 ? m.BodyPreview : m.BodyText)),
        Url = m.WebLink,
        Author = Author(m.From),
        CreatedAt = m.SentDateTime,
        ModifiedAt = m.ReceivedDateTime,
        Score = score,
        Metadata = new()
        {
            ["from"] = Format(m.From),
            ["to"] = m.ToRecipients.Select(Format).ToArray(),
            ["cc"] = m.CcRecipients.Select(Format).ToArray(),
            ["received_at"] = m.ReceivedDateTime,
            ["has_attachments"] = m.HasAttachments,
            ["conversation_id"] = m.ConversationId,
            ["is_read"] = m.IsRead,
            ["importance"] = m.Importance,
        },
    };

    public ResultItem Teams(GraphChatMessage m, double score, string? snippetOverride = null)
    {
        var isChannel = m.TeamId is not null || m.ChannelId is not null;
        var metadata = new Dictionary<string, object?>();
        if (isChannel)
        {
            metadata["team_id"] = m.TeamId;
            metadata["team_name"] = m.TeamName;
            metadata["channel_id"] = m.ChannelId;
            metadata["channel_name"] = m.ChannelName;
        }
        else
        {
            metadata["chat_id"] = m.ChatId;
        }
        metadata["chat_topic"] = m.ChatTopic;
        metadata["thread_root_id"] = m.ReplyToId ?? m.Id;
        metadata["reply_count"] = m.ReplyCount;
        metadata["mentions"] = m.Mentions.Select(Format).ToArray();

        var title = isChannel
            ? $"{m.TeamName ?? "Team"} > {m.ChannelName ?? "Channel"}"
            : m.ChatTopic ?? $"Chat with {m.From.Name}";

        return new ResultItem
        {
            Id = $"{Sources.Teams}:{m.Id}",
            Source = Sources.Teams,
            Type = isChannel ? "channel_message" : "chat_message",
            Title = title,
            Snippet = Snippet(snippetOverride ?? m.Body),
            Url = m.WebUrl,
            Author = Author(m.From),
            CreatedAt = m.CreatedDateTime,
            ModifiedAt = m.CreatedDateTime,
            Score = score,
            Metadata = metadata,
        };
    }

    public ResultItem File(GraphDriveItem f, double score, string? snippetOverride = null) => new()
    {
        Id = $"{Sources.Files}:{f.Id}",
        Source = Sources.Files,
        Type = "document",
        Title = f.Name,
        Snippet = Snippet(snippetOverride ?? f.Summary),
        Url = f.WebUrl,
        Author = Author(f.CreatedBy),
        CreatedAt = f.CreatedDateTime,
        ModifiedAt = f.LastModifiedDateTime,
        Score = score,
        Metadata = new()
        {
            ["drive_id"] = f.DriveId,
            ["item_id"] = f.Id,
            ["site"] = f.SiteName,
            ["path"] = f.Path,
            ["file_type"] = f.FileType,
            ["size"] = f.Size,
            ["last_modified_by"] = f.LastModifiedBy is null ? null : Format(f.LastModifiedBy),
            ["web_url"] = f.WebUrl,
        },
    };

    public ResultItem CopilotChat(AiInteraction i, double score) => new()
    {
        Id = $"{Sources.CopilotChat}:{i.Id}",
        Source = Sources.CopilotChat,
        Type = "copilot_interaction",
        Title = $"Copilot {i.AppClass} {(i.InteractionType == "aiResponse" ? "response" : "prompt")}",
        Snippet = Snippet(i.Body),
        Url = i.WebUrl,
        CreatedAt = i.CreatedDateTime,
        ModifiedAt = i.CreatedDateTime,
        Score = score,
        Metadata = new()
        {
            ["conversation_id"] = i.SessionId,
            ["app"] = i.AppClass,
            ["role"] = i.InteractionType == "aiResponse" ? "assistant" : "user",
            ["session_id"] = i.SessionId,
        },
    };

    public ResultItem Event(GraphEvent e, double score, string? snippetOverride = null) => new()
    {
        Id = $"{Sources.Calendar}:{e.Id}",
        Source = Sources.Calendar,
        Type = "event",
        Title = e.Subject,
        Snippet = Snippet(snippetOverride ?? e.BodyPreview),
        Url = e.WebLink,
        Author = Author(e.Organizer),
        CreatedAt = e.Start,
        Score = score,
        Metadata = new()
        {
            ["start"] = e.Start,
            ["end"] = e.End,
            ["attendees"] = e.Attendees.Select(Format).ToArray(),
            ["is_online_meeting"] = e.IsOnlineMeeting,
        },
    };
}
