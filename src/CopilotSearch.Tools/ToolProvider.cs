using System.Reflection;
using CopilotSearch.Contracts;
using Microsoft.Extensions.AI;

namespace CopilotSearch.Tools;

public sealed class ToolProvider : IToolProvider
{
    private readonly IReadOnlyList<AITool> _tools;

    public ToolProvider(
        IdentityTools identity,
        SearchAllTool searchAll,
        MailTools mail,
        TeamsTools teams,
        FilesTools files,
        CopilotChatTools chats)
    {
        _tools =
        [
            Create(identity, nameof(IdentityTools.GetCurrentUser), ToolNames.GetCurrentUser),
            Create(identity, nameof(IdentityTools.GetCurrentDatetime), ToolNames.GetCurrentDatetime),
            Create(identity, nameof(IdentityTools.ResolvePerson), ToolNames.ResolvePerson),
            Create(searchAll, nameof(SearchAllTool.SearchAll), ToolNames.SearchAll),
            Create(mail, nameof(MailTools.SearchEmails), ToolNames.SearchEmails),
            Create(mail, nameof(MailTools.GetEmail), ToolNames.GetEmail),
            Create(teams, nameof(TeamsTools.SearchTeamsMessages), ToolNames.SearchTeamsMessages),
            Create(files, nameof(FilesTools.SearchFiles), ToolNames.SearchFiles),
            Create(files, nameof(FilesTools.GetFileContent), ToolNames.GetFileContent),
            Create(chats, nameof(CopilotChatTools.SearchCopilotChats), ToolNames.SearchCopilotChats),
        ];
    }

    public IReadOnlyList<AITool> GetTools() => _tools;

    private static AIFunction Create(object target, string methodName, string toolName)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Tool method {methodName} not found on {target.GetType().Name}.");
        return new ArgumentSafeFunction(AIFunctionFactory.Create(method, target, new AIFunctionFactoryOptions { Name = toolName }));
    }
}
