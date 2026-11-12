using Microsoft.Extensions.AI;

namespace CopilotSearch.Contracts;

/// <summary>POC: hardcoded demo user. P1: derived from the validated token.</summary>
public interface IUserContext
{
    string UserId { get; }
    string DisplayName { get; }
    string Email { get; }
    string TimeZone { get; } // IANA
}

public sealed class DemoUserContext : IUserContext
{
    public string UserId => "u-akshay";
    public string DisplayName => "Akshay Dagar";
    public string Email => "akshay@contoso.com";
    public string TimeZone => "Asia/Kolkata";
}

/// <summary>Bound to configuration section "Tools".</summary>
public sealed class ToolOptions
{
    public const string SectionName = "Tools";
    public int DefaultTimeoutSeconds { get; set; } = 30;
    /// <summary>Per tool name overrides, e.g. "search_emails": 10.</summary>
    public Dictionary<string, int> TimeoutSeconds { get; set; } = new();
    public int DefaultTop { get; set; } = 10;
    public int MaxTop { get; set; } = 25;
    public int MaxSnippetChars { get; set; } = 300;

    public TimeSpan TimeoutFor(string toolName) =>
        TimeSpan.FromSeconds(TimeoutSeconds.TryGetValue(toolName, out var s) ? s : DefaultTimeoutSeconds);
}

/// <summary>
/// Entry point Orchestration and Mcp use to get the tool catalog (Part 2 implements,
/// Part 1 uses a stub implementation until then). Tools return objects that serialize to
/// SearchEnvelope / ErrorEnvelope / tool-specific JSON.
/// </summary>
public interface IToolProvider
{
    IReadOnlyList<AITool> GetTools();
}

public static class ToolNames
{
    public const string GetCurrentUser = "get_current_user";
    public const string GetCurrentDatetime = "get_current_datetime";
    public const string ResolvePerson = "resolve_person";
    public const string SearchAll = "search_all";
    public const string SearchEmails = "search_emails";
    public const string GetEmail = "get_email";
    public const string SearchTeamsMessages = "search_teams_messages";
    public const string SearchFiles = "search_files";
    public const string GetFileContent = "get_file_content";
    public const string SearchCopilotChats = "search_copilot_chats";

    public static readonly IReadOnlyList<string> All =
    [
        GetCurrentUser, GetCurrentDatetime, ResolvePerson, SearchAll, SearchEmails,
        GetEmail, SearchTeamsMessages, SearchFiles, GetFileContent, SearchCopilotChats
    ];
}
