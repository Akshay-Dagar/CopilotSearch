using System.Text.Json;
using CopilotSearch.Contracts;

namespace CopilotSearch.Tools.Tests;

public class MailToolsTests
{
    [Fact]
    public async Task SearchEmails_HappyPath_MapsItem()
    {
        var f = new FakeConnectors();
        f.MailHits.Add(new MailHit(FakeConnectors.Msg("m1"), 0.9));
        var env = Assert.IsType<SearchEnvelope>(await T.Mail(f).SearchEmails(query: "budget", from: "priya@contoso.com",
            start: "2026-11-01T00:00:00Z", folder: "INBOX", importance: "High", sort: "date_desc"));

        var item = Assert.Single(env.Results);
        Assert.Equal("mail:m1", item.Id);
        Assert.Equal("mail", item.Source);
        Assert.Equal("email", item.Type);
        Assert.Equal("Preview of Q3 budget", item.Snippet);
        Assert.Equal("https://outlook/m1", item.Url);
        Assert.Equal("priya@contoso.com", item.Author!.Email);
        Assert.Equal(0.9, item.Score);
        Assert.Equal("Priya <priya@contoso.com>", item.Metadata["from"]);
        Assert.Equal(["cc@contoso.com"], (string[])item.Metadata["cc"]!);
        Assert.Equal("c1", item.Metadata["conversation_id"]);
        Assert.Equal(1, env.Returned);
        Assert.False(env.Truncated);

        Assert.Equal("inbox", f.LastMail!.Folder);
        Assert.Equal("high", f.LastMail.Importance);
        Assert.Equal("date_desc", f.LastMail.Sort);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), f.LastMail.Range.Start);

        var json = JsonSerializer.Serialize<object>(env);
        Assert.Contains("\"total_estimate\"", json);
        Assert.Contains("\"created_at\"", json);
    }

    [Theory]
    [InlineData(null, 10)]
    [InlineData(0, 1)]
    [InlineData(-4, 1)]
    [InlineData(7, 7)]
    [InlineData(500, 25)]
    public async Task SearchEmails_ClampsTop(int? top, int expected)
    {
        var f = new FakeConnectors();
        await T.Mail(f).SearchEmails(query: "x", top: top);
        Assert.Equal(expected, f.LastMail!.Top);
    }

    [Fact]
    public async Task SearchEmails_RespectsConfiguredLimits()
    {
        var f = new FakeConnectors();
        await T.Mail(f, o => { o.DefaultTop = 3; o.MaxTop = 4; }).SearchEmails(query: "x", top: 50);
        Assert.Equal(4, f.LastMail!.Top);
    }

    [Fact]
    public async Task SearchEmails_RequiresSomeFilter()
    {
        var f = new FakeConnectors();
        T.Err(await T.Mail(f).SearchEmails(), ToolErrorCodes.InvalidArgument);
        T.Err(await T.Mail(f).SearchEmails(sort: "date_asc", top: 3), ToolErrorCodes.InvalidArgument);
        Assert.Null(f.LastMail);
        Assert.IsType<SearchEnvelope>(await T.Mail(f).SearchEmails(is_unread: true));
        Assert.IsType<SearchEnvelope>(await T.Mail(f).SearchEmails(folder: "sent"));
    }

    [Theory]
    [InlineData("not-a-date", null)]
    [InlineData(null, "31/31/2026")]
    public async Task SearchEmails_InvalidDate(string? start, string? end)
    {
        T.Err(await T.Mail(new FakeConnectors()).SearchEmails(query: "x", start: start, end: end), ToolErrorCodes.InvalidArgument);
    }

    [Fact]
    public async Task SearchEmails_StartAfterEnd_IsInvalid()
    {
        T.Err(await T.Mail(new FakeConnectors()).SearchEmails(query: "x", start: "2026-11-05", end: "2026-11-01"), ToolErrorCodes.InvalidArgument);
    }

    [Theory]
    [InlineData("folder", "trash")]
    [InlineData("importance", "urgent")]
    [InlineData("sort", "newest")]
    public async Task SearchEmails_InvalidEnum(string param, string value)
    {
        var tool = T.Mail(new FakeConnectors());
        var result = param switch
        {
            "folder" => await tool.SearchEmails(query: "x", folder: value),
            "importance" => await tool.SearchEmails(query: "x", importance: value),
            _ => await tool.SearchEmails(query: "x", sort: value),
        };
        var e = T.Err(result, ToolErrorCodes.InvalidArgument);
        Assert.Contains(param, e.Error.Message);
    }

    [Fact]
    public async Task SearchEmails_TruncatedWhenMoreAvailable_AndSnippetLimited()
    {
        var f = new FakeConnectors { MailTotal = 40 };
        f.MailHits.Add(new MailHit(FakeConnectors.Msg("m1") with { BodyPreview = new string('a', 1000) }, 1));
        var env = Assert.IsType<SearchEnvelope>(await T.Mail(f).SearchEmails(query: "x"));
        Assert.True(env.Truncated);
        Assert.Equal(40, env.TotalEstimate);
        Assert.Equal(300, env.Results[0].Snippet.Length);
    }

    [Fact]
    public async Task SearchEmails_Timeout()
    {
        var f = new FakeConnectors { Delay = TimeSpan.FromSeconds(5) };
        var tool = T.Mail(f, o => { o.DefaultTimeoutSeconds = 30; o.TimeoutSeconds[ToolNames.SearchEmails] = 0; });
        var e = T.Err(await tool.SearchEmails(query: "x"), ToolErrorCodes.Timeout);
        Assert.True(e.Error.Retryable);
    }

    [Fact]
    public async Task SearchEmails_UpstreamException()
    {
        var f = new FakeConnectors { Throw = new HttpRequestException("503") };
        var e = T.Err(await T.Mail(f).SearchEmails(query: "x"), ToolErrorCodes.UpstreamError);
        Assert.Contains("503", e.Error.Message);
    }

    [Fact]
    public async Task GetEmail_StripsPrefix_AndReturnsFullBody()
    {
        var f = new FakeConnectors();
        f.Messages["m1"] = FakeConnectors.Msg("m1");
        var r = Assert.IsType<EmailResult>(await T.Mail(f).GetEmail("mail:m1"));
        Assert.Equal("m1", f.LastMessageId);
        Assert.Equal("mail:m1", r.Id);
        Assert.Equal("Full body text", r.BodyText);
        Assert.False(r.Truncated);
        Assert.Null(r.Thread);
        Assert.Equal("d1:i1", Assert.Single(r.Attachments).FileId);
        Assert.Contains("\"body_text\"", JsonSerializer.Serialize<object>(r));
    }

    [Fact]
    public async Task GetEmail_TruncatesBody()
    {
        var f = new FakeConnectors();
        f.Messages["m1"] = FakeConnectors.Msg("m1", body: new string('x', 100));
        var r = Assert.IsType<EmailResult>(await T.Mail(f).GetEmail("m1", max_chars: 10));
        Assert.Equal(10, r.BodyText.Length);
        Assert.True(r.Truncated);
    }

    [Fact]
    public async Task GetEmail_IncludeThread_ExcludesSelf()
    {
        var f = new FakeConnectors();
        f.Messages["m1"] = FakeConnectors.Msg("m1");
        f.Thread.AddRange([FakeConnectors.Msg("m1"), FakeConnectors.Msg("m2", "Re: Q3 budget")]);
        var r = Assert.IsType<EmailResult>(await T.Mail(f).GetEmail("m1", include_thread: true));
        Assert.Equal("mail:m2", Assert.Single(r.Thread!).Id);
    }

    [Fact]
    public async Task GetEmail_NotFound_And_BlankId()
    {
        var tool = T.Mail(new FakeConnectors());
        T.Err(await tool.GetEmail("nope"), ToolErrorCodes.NotFound);
        T.Err(await tool.GetEmail(" "), ToolErrorCodes.InvalidArgument);
    }
}
