using CopilotSearch.Contracts;

namespace CopilotSearch.Tools.Tests;

public class TeamsChatsTests
{
    [Fact]
    public async Task SearchTeams_MapsChatAndChannelMessages()
    {
        var f = new FakeConnectors();
        f.TeamsHits.Add(new TeamsHit(FakeConnectors.Chat("a"), 0.5));
        f.TeamsHits.Add(new TeamsHit(FakeConnectors.Chat("b", channel: true), 0.7));
        var env = Assert.IsType<SearchEnvelope>(await T.Teams(f).SearchTeamsMessages(query: "hello", scope: "Any"));

        var chat = env.Results[0];
        Assert.Equal("teams:a", chat.Id);
        Assert.Equal("chat_message", chat.Type);
        Assert.Equal("chat1", chat.Metadata["chat_id"]);
        Assert.False(chat.Metadata.ContainsKey("team_id"));
        Assert.Equal("a", chat.Metadata["thread_root_id"]);
        Assert.Equal(2, chat.Metadata["reply_count"]);

        var channel = env.Results[1];
        Assert.Equal("channel_message", channel.Type);
        Assert.Equal("t1", channel.Metadata["team_id"]);
        Assert.Equal("ch1", channel.Metadata["channel_id"]);
        Assert.Equal("Launch > General", channel.Title);
    }

    [Fact]
    public async Task SearchTeams_ChatScopeIgnoresTeamAndChannelWithWarning()
    {
        var f = new FakeConnectors();
        var env = Assert.IsType<SearchEnvelope>(await T.Teams(f).SearchTeamsMessages(scope: "chat", team: "Launch", channel: "General"));
        Assert.Single(env.Warnings);
        Assert.Null(f.LastTeams!.Team);
        Assert.Null(f.LastTeams.Channel);
    }

    [Fact]
    public async Task SearchTeams_InvalidScope_And_Clamp()
    {
        var f = new FakeConnectors();
        T.Err(await T.Teams(f).SearchTeamsMessages(scope: "dm"), ToolErrorCodes.InvalidArgument);
        await T.Teams(f).SearchTeamsMessages(top: 1000);
        Assert.Equal(25, f.LastTeams!.Top);
    }

    [Fact]
    public async Task SearchTeams_Timeout_And_Upstream()
    {
        var slow = new FakeConnectors { Delay = TimeSpan.FromSeconds(5) };
        T.Err(await T.Teams(slow, o => o.TimeoutSeconds[ToolNames.SearchTeamsMessages] = 0).SearchTeamsMessages(query: "x"), ToolErrorCodes.Timeout);
        var broken = new FakeConnectors { Throw = new Exception("x") };
        T.Err(await T.Teams(broken).SearchTeamsMessages(query: "x"), ToolErrorCodes.UpstreamError);
    }

    [Fact]
    public async Task SearchCopilotChats_MapsInteraction()
    {
        var f = new FakeConnectors();
        f.ChatHits.Add(new AiInteractionHit(new AiInteraction { Id = "i1", SessionId = "s1", AppClass = "word", InteractionType = "aiResponse", Body = "Here is a summary", CreatedDateTime = FakeConnectors.T0 }, 0.4));
        var env = Assert.IsType<SearchEnvelope>(await T.Chats(f).SearchCopilotChats(query: "summary", app: "WORD"));
        var item = Assert.Single(env.Results);
        Assert.Equal("copilot_chat:i1", item.Id);
        Assert.Equal("copilot_interaction", item.Type);
        Assert.Equal("assistant", item.Metadata["role"]);
        Assert.Equal("word", item.Metadata["app"]);
        Assert.Equal("s1", item.Metadata["conversation_id"]);
        Assert.Equal("word", f.LastChats!.App);
    }

    [Fact]
    public async Task SearchCopilotChats_InvalidApp_Date_And_Timeout()
    {
        var f = new FakeConnectors();
        T.Err(await T.Chats(f).SearchCopilotChats(app: "onenote"), ToolErrorCodes.InvalidArgument);
        T.Err(await T.Chats(f).SearchCopilotChats(start: "yesterday-ish"), ToolErrorCodes.InvalidArgument);
        f.Delay = TimeSpan.FromSeconds(5);
        T.Err(await T.Chats(f, o => o.TimeoutSeconds[ToolNames.SearchCopilotChats] = 0).SearchCopilotChats(query: "x"), ToolErrorCodes.Timeout);
    }
}

public class FilesToolsTests
{
    [Fact]
    public async Task SearchFiles_MapsMetadata_AndNormalizesFileTypes()
    {
        var f = new FakeConnectors();
        f.FileHits.Add(new FileHit(FakeConnectors.Drive("i1"), 0.8));
        var env = Assert.IsType<SearchEnvelope>(await T.Files(f).SearchFiles(query: "budget", file_types: [".XLSX", " pdf ", "xlsx", ""], location: "SharePoint"));

        var item = Assert.Single(env.Results);
        Assert.Equal("files:i1", item.Id);
        Assert.Equal("document", item.Type);
        Assert.Equal("Budget summary", item.Snippet);
        Assert.Equal("d1", item.Metadata["drive_id"]);
        Assert.Equal("i1", item.Metadata["item_id"]);
        Assert.Equal("Finance", item.Metadata["site"]);
        Assert.Equal(99L, item.Metadata["size"]);
        Assert.Equal(["xlsx", "pdf"], f.LastFiles!.FileTypes);
        Assert.Equal("sharepoint", f.LastFiles.Location);
    }

    [Fact]
    public async Task SearchFiles_InvalidLocation_And_EmptyTypesBecomeNull()
    {
        var f = new FakeConnectors();
        T.Err(await T.Files(f).SearchFiles(location: "dropbox"), ToolErrorCodes.InvalidArgument);
        await T.Files(f).SearchFiles(file_types: [" "]);
        Assert.Null(f.LastFiles!.FileTypes);
    }

    [Fact]
    public async Task GetFileContent_TruncatesAndReportsCounts()
    {
        var f = new FakeConnectors();
        f.FileContents[("d1", "i1")] = new FileContent { DriveId = "d1", ItemId = "i1", Name = "Budget.xlsx", FileType = "xlsx", Text = new string('z', 500) };
        var r = Assert.IsType<FileContentResult>(await T.Files(f).GetFileContent("d1", "files:i1", max_chars: 100, page_range: "1-3"));
        Assert.Equal("files:i1", r.Id);
        Assert.Equal(100, r.Text.Length);
        Assert.Equal(100, r.ExtractedChars);
        Assert.Equal(500, r.TotalChars);
        Assert.True(r.Truncated);

        r = Assert.IsType<FileContentResult>(await T.Files(f).GetFileContent("d1", "i1"));
        Assert.Equal(500, r.Text.Length);
        Assert.False(r.Truncated);
    }

    [Fact]
    public async Task GetFileContent_NotFound_Invalid_Timeout()
    {
        var f = new FakeConnectors();
        T.Err(await T.Files(f).GetFileContent("d1", "missing"), ToolErrorCodes.NotFound);
        T.Err(await T.Files(f).GetFileContent("", "i1"), ToolErrorCodes.InvalidArgument);
        T.Err(await T.Files(f).GetFileContent("d1", "i1", page_range: "abc"), ToolErrorCodes.InvalidArgument);
        f.Delay = TimeSpan.FromSeconds(5);
        T.Err(await T.Files(f, o => o.TimeoutSeconds[ToolNames.GetFileContent] = 0).GetFileContent("d1", "i1"), ToolErrorCodes.Timeout);
    }

    [Fact]
    public async Task GetFileContent_UpstreamError()
    {
        var f = new FakeConnectors { Throw = new IOException("disk") };
        T.Err(await T.Files(f).GetFileContent("d1", "i1"), ToolErrorCodes.UpstreamError);
    }
}
