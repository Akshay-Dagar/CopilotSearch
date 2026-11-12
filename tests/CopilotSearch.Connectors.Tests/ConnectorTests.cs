using CopilotSearch.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace CopilotSearch.Connectors.Tests;

public class MailConnectorTests
{
    private static readonly IMailConnector Mail = TestHost.Mail;

    private static Task<SearchPage<MailHit>> Search(MailSearchRequest r) => Mail.SearchAsync(r with { Top = 100 }, CancellationToken.None);

    [Fact]
    public async Task Budget_query_ranks_budget_mail_first()
    {
        var page = await Mail.SearchAsync(new MailSearchRequest { Query = "Q3 budget final approval", Top = 5 }, CancellationToken.None);
        Assert.Equal("ACTION REQUIRED: Q3 budget final approval by Friday", page.Items[0].Message.Subject);
        Assert.True(page.Items.Zip(page.Items.Skip(1)).All(p => p.First.Score >= p.Second.Score));
    }

    [Fact]
    public async Task Natural_language_query_strips_stopwords()
    {
        var page = await Mail.SearchAsync(new MailSearchRequest { Query = "What did Priya say about the Q3 budget?", From = "priya.sharma", Top = 3 }, CancellationToken.None);
        Assert.NotEmpty(page.Items);
        Assert.All(page.Items, h => Assert.Equal("priya.sharma@contoso.com", h.Message.From.Address));
        Assert.Contains("Q3 budget", page.Items[0].Message.Subject);
    }

    [Fact]
    public async Task Total_counts_before_top()
    {
        var page = await Mail.SearchAsync(new MailSearchRequest { Top = 3 }, CancellationToken.None);
        Assert.Equal(3, page.Items.Count);
        Assert.Equal(TestHost.Fixtures.Messages.Count, page.Total);
    }

    [Fact]
    public async Task Filter_only_query_orders_by_date_desc()
    {
        var page = await Search(new MailSearchRequest { From = "olivia" });
        Assert.NotEmpty(page.Items);
        Assert.Equal(page.Items.OrderByDescending(h => h.Message.ReceivedDateTime).Select(h => h.Message.Id), page.Items.Select(h => h.Message.Id));
    }

    [Fact]
    public async Task From_matches_name_or_email_case_insensitively()
    {
        var byName = await Search(new MailSearchRequest { From = "OLIVIA MARTIN" });
        var byMail = await Search(new MailSearchRequest { From = "Olivia@Contoso.com" });
        Assert.NotEmpty(byName.Items);
        Assert.Equal(byName.Items.Select(h => h.Message.Id), byMail.Items.Select(h => h.Message.Id));
    }

    [Fact]
    public async Task To_and_cc_filters_work()
    {
        var to = await Search(new MailSearchRequest { To = "nina" });
        Assert.NotEmpty(to.Items);
        Assert.All(to.Items, h => Assert.Contains(h.Message.ToRecipients, r => r.Address.Contains("nina")));

        var cc = await Search(new MailSearchRequest { Cc = "Olivia Martin" });
        Assert.NotEmpty(cc.Items);
        Assert.All(cc.Items, h => Assert.Contains(h.Message.CcRecipients, r => r.Name == "Olivia Martin"));
    }

    [Fact]
    public async Task Date_range_is_inclusive()
    {
        var all = await Search(new MailSearchRequest());
        var target = all.Items.First().Message.ReceivedDateTime;
        var exact = await Search(new MailSearchRequest { Range = new DateRange(target, target) });
        Assert.Contains(exact.Items, h => h.Message.ReceivedDateTime == target);

        var october = await Search(new MailSearchRequest { Range = new DateRange(TestHost.Day(10, 1), TestHost.Day(10, 31).AddDays(1).AddTicks(-1)) });
        Assert.NotEmpty(october.Items);
        Assert.All(october.Items, h => Assert.Equal(10, h.Message.ReceivedDateTime.Month));
    }

    [Fact]
    public async Task Folder_filter_works()
    {
        var sent = await Search(new MailSearchRequest { Folder = "sent" });
        Assert.NotEmpty(sent.Items);
        Assert.All(sent.Items, h => Assert.Equal("akshay@contoso.com", h.Message.From.Address));
        var drafts = await Search(new MailSearchRequest { Folder = "Drafts" });
        Assert.Single(drafts.Items);
        var archive = await Search(new MailSearchRequest { Folder = "archive" });
        Assert.All(archive.Items, h => Assert.Equal("archive", h.Message.ParentFolder));
    }

    [Fact]
    public async Task Attachment_importance_and_unread_filters_work()
    {
        var withAtt = await Search(new MailSearchRequest { HasAttachments = true });
        Assert.All(withAtt.Items, h => Assert.True(h.Message.HasAttachments));
        var without = await Search(new MailSearchRequest { HasAttachments = false });
        Assert.All(without.Items, h => Assert.False(h.Message.HasAttachments));
        Assert.Equal(TestHost.Fixtures.Messages.Count, withAtt.Total + without.Total);

        var urgent = await Search(new MailSearchRequest { Importance = "HIGH", IsUnread = true, HasAttachments = true });
        var hit = Assert.Single(urgent.Items);
        Assert.Contains("final approval", hit.Message.Subject);

        var read = await Search(new MailSearchRequest { IsUnread = false });
        Assert.All(read.Items, h => Assert.True(h.Message.IsRead));
    }

    [Fact]
    public async Task Sort_overrides_relevance()
    {
        var asc = await Search(new MailSearchRequest { Query = "atlas", Sort = "date_asc" });
        Assert.Equal(asc.Items.OrderBy(h => h.Message.ReceivedDateTime).Select(h => h.Message.Id), asc.Items.Select(h => h.Message.Id));
        var desc = await Search(new MailSearchRequest { Query = "atlas", Sort = "date_desc" });
        Assert.Equal(desc.Items.OrderByDescending(h => h.Message.ReceivedDateTime).Select(h => h.Message.Id), desc.Items.Select(h => h.Message.Id));
    }

    [Fact]
    public async Task Unmatched_query_returns_nothing()
    {
        var page = await Search(new MailSearchRequest { Query = "zzzxqv" });
        Assert.Empty(page.Items);
        Assert.Equal(0, page.Total);
    }

    [Fact]
    public async Task Injection_mail_is_searchable()
    {
        var page = await Search(new MailSearchRequest { Query = "invoice support terms" });
        Assert.Contains("Ignore all previous instructions", page.Items[0].Message.BodyText);
    }

    [Fact]
    public async Task Get_and_thread_lookup()
    {
        var any = TestHost.Fixtures.Messages.First(m => m.ConversationId == "c-outage-notice");
        Assert.Equal(any.Id, (await Mail.GetAsync(any.Id, CancellationToken.None))!.Id);
        Assert.Null(await Mail.GetAsync("does-not-exist", CancellationToken.None));

        var thread = await Mail.ListThreadAsync("c-outage-notice", CancellationToken.None);
        Assert.Equal(2, thread.Count);
        Assert.True(thread[0].SentDateTime <= thread[1].SentDateTime);
        Assert.Empty(await Mail.ListThreadAsync("nope", CancellationToken.None));
    }
}

public class TeamsConnectorTests
{
    private static Task<SearchPage<TeamsHit>> Search(TeamsSearchRequest r) => TestHost.Teams.SearchAsync(r with { Top = 100 }, CancellationToken.None);

    [Fact]
    public async Task Topical_query_ranks_expected_message_first()
    {
        var page = await TestHost.Teams.SearchAsync(new TeamsSearchRequest { Query = "export API fix slipping GA", Top = 3 }, CancellationToken.None);
        Assert.Contains("export API fix is slipping", page.Items[0].Message.Body);
    }

    [Fact]
    public async Task Scope_filter_splits_chat_and_channel()
    {
        var chats = await Search(new TeamsSearchRequest { Scope = "chat" });
        var channels = await Search(new TeamsSearchRequest { Scope = "channel" });
        Assert.All(chats.Items, h => Assert.NotNull(h.Message.ChatId));
        Assert.All(channels.Items, h => Assert.NotNull(h.Message.TeamId));
        Assert.Equal(TestHost.Fixtures.ChatMessages.Count, chats.Total + channels.Total);
    }

    [Fact]
    public async Task Team_and_channel_filters_work()
    {
        var team = await Search(new TeamsSearchRequest { Team = "engineering" });
        Assert.NotEmpty(team.Items);
        Assert.All(team.Items, h => Assert.Equal("Engineering", h.Message.TeamName));
        var channel = await Search(new TeamsSearchRequest { Team = "Engineering", Channel = "INCIDENT" });
        Assert.NotEmpty(channel.Items);
        Assert.All(channel.Items, h => Assert.Equal("incident-response", h.Message.ChannelName));
    }

    [Fact]
    public async Task From_and_mentions_filters_work()
    {
        var from = await Search(new TeamsSearchRequest { From = "sarah" });
        Assert.NotEmpty(from.Items);
        Assert.All(from.Items, h => Assert.Equal("Sarah Chen", h.Message.From.Name));

        var mentions = await Search(new TeamsSearchRequest { Mentions = "akshay@contoso.com" });
        Assert.NotEmpty(mentions.Items);
        Assert.All(mentions.Items, h => Assert.Contains(h.Message.Mentions, m => m.Name == "Akshay Dagar"));
    }

    [Fact]
    public async Task Date_range_filter_works()
    {
        var recent = await Search(new TeamsSearchRequest { Range = new DateRange(TestHost.Day(11, 5), null) });
        Assert.NotEmpty(recent.Items);
        Assert.All(recent.Items, h => Assert.True(h.Message.CreatedDateTime >= TestHost.Day(11, 5)));
    }

    [Fact]
    public async Task Threads_are_visible_with_reply_info()
    {
        var page = await Search(new TeamsSearchRequest { Channel = "atlas-launch" });
        Assert.Contains(page.Items, h => h.Message.ReplyCount > 0);
        Assert.Contains(page.Items, h => h.Message.ReplyToId is not null);
    }
}

public class FilesConnectorTests
{
    private static Task<SearchPage<FileHit>> Search(FileSearchRequest r) => TestHost.Files.SearchAsync(r with { Top = 100 }, CancellationToken.None);

    [Fact]
    public async Task Topical_query_ranks_expected_file_first()
    {
        var page = await TestHost.Files.SearchAsync(new FileSearchRequest { Query = "auth outage postmortem", Top = 3 }, CancellationToken.None);
        Assert.Equal("Auth-Outage-Postmortem-2026-10-14.docx", page.Items[0].Item.Name);
    }

    [Fact]
    public async Task Content_text_is_searchable()
    {
        var page = await Search(new FileSearchRequest { Query = "shortfall penalty auto-renews" });
        Assert.Equal("Vendor-Cloud-Commitment-Contract.pdf", page.Items[0].Item.Name);
    }

    [Fact]
    public async Task File_type_filter_accepts_dots_and_case()
    {
        var page = await Search(new FileSearchRequest { FileTypes = [".XLSX", "pdf"] });
        Assert.NotEmpty(page.Items);
        Assert.All(page.Items, h => Assert.Contains(h.Item.FileType, new[] { "xlsx", "pdf" }));
    }

    [Fact]
    public async Task Location_site_author_and_modifier_filters_work()
    {
        var onedrive = await Search(new FileSearchRequest { Location = "onedrive" });
        Assert.NotEmpty(onedrive.Items);
        Assert.All(onedrive.Items, h => Assert.Equal("onedrive", h.Item.Location));

        var site = await Search(new FileSearchRequest { Site = "atlas" });
        Assert.NotEmpty(site.Items);
        Assert.All(site.Items, h => Assert.Equal("Atlas Program", h.Item.SiteName));

        var author = await Search(new FileSearchRequest { Author = "Sarah Chen" });
        Assert.NotEmpty(author.Items);
        Assert.All(author.Items, h => Assert.Equal("Sarah Chen", h.Item.CreatedBy!.Name));

        var modifiedBy = await Search(new FileSearchRequest { Author = "elena", ModifiedBy = "akshay" });
        Assert.Contains(modifiedBy.Items, h => h.Item.Name == "Atlas-Launch-Plan.docx");
    }

    [Fact]
    public async Task Date_range_uses_last_modified()
    {
        var page = await Search(new FileSearchRequest { Range = new DateRange(TestHost.Day(11, 1), TestHost.Day(11, 12)) });
        Assert.NotEmpty(page.Items);
        Assert.All(page.Items, h => Assert.True(h.Item.LastModifiedDateTime >= TestHost.Day(11, 1)));
    }

    [Fact]
    public async Task GetContent_returns_text_or_null()
    {
        var item = TestHost.Fixtures.DriveItems.Single(d => d.Name == "Auth-Outage-Postmortem-2026-10-14.docx");
        var content = await TestHost.Files.GetContentAsync(item.DriveId, item.Id, CancellationToken.None);
        Assert.NotNull(content);
        Assert.True(content!.Text.Length > 5000);
        Assert.Equal("docx", content.FileType);
        Assert.Null(await TestHost.Files.GetContentAsync(item.DriveId, "missing", CancellationToken.None));
        Assert.Null(await TestHost.Files.GetContentAsync("missing", item.Id, CancellationToken.None));
    }
}

public class CopilotChatConnectorTests
{
    [Fact]
    public async Task App_filter_and_query_work()
    {
        var excel = await TestHost.Copilot.SearchAsync(new CopilotChatQuery { App = "excel", Top = 50 }, CancellationToken.None);
        Assert.NotEmpty(excel.Items);
        Assert.All(excel.Items, h => Assert.Equal("excel", h.Interaction.AppClass));

        var page = await TestHost.Copilot.SearchAsync(new CopilotChatQuery { Query = "hiring Senior Backend Engineer offer", Top = 2 }, CancellationToken.None);
        Assert.Equal("bizchat", page.Items[0].Interaction.AppClass);
        Assert.Contains("hiring", page.Items[0].Interaction.Body);
    }

    [Fact]
    public async Task Range_filter_and_total_work()
    {
        var page = await TestHost.Copilot.SearchAsync(
            new CopilotChatQuery { Range = new DateRange(TestHost.Day(11, 1), null), Top = 1 }, CancellationToken.None);
        Assert.Single(page.Items);
        Assert.True(page.Total > 1);
        var all = await TestHost.Copilot.SearchAsync(new CopilotChatQuery { Top = 100 }, CancellationToken.None);
        Assert.Equal(TestHost.Fixtures.Interactions.Count, all.Total);
    }
}

public class PeopleConnectorTests
{
    [Fact]
    public async Task GetMe_returns_demo_user()
    {
        var me = await TestHost.People.GetMeAsync(CancellationToken.None);
        Assert.Equal("u-akshay", me.Id);
        Assert.Equal("Akshay Dagar", me.DisplayName);
        Assert.Equal("Asia/Kolkata", me.TimeZone);
        Assert.Equal("Elena Rossi", me.Manager!.Name);
    }

    [Fact]
    public async Task Shared_first_name_returns_both_candidates()
    {
        var people = await TestHost.People.ResolveAsync("Priya", 5, CancellationToken.None);
        Assert.Equal(2, people.Count);
        Assert.Equal(["Priya Nair", "Priya Sharma"], people.Select(p => p.DisplayName).Order());
    }

    [Fact]
    public async Task Full_name_disambiguates()
    {
        var people = await TestHost.People.ResolveAsync("priya sharma", 5, CancellationToken.None);
        Assert.Equal("Priya Sharma", people[0].DisplayName);
        Assert.DoesNotContain(people, p => p.DisplayName == "Priya Nair");
    }

    [Theory]
    [InlineData("Sharma", "Priya Sharma")]
    [InlineData("liam", "Liam O'Connor")]
    [InlineData("grace@contoso.com", "Grace Lee")]
    [InlineData("Sara Chen", "Sarah Chen")]
    [InlineData("fat", "Fatima Al-Hassan")]
    public async Task Fuzzy_resolution(string query, string expected)
    {
        var people = await TestHost.People.ResolveAsync(query, 3, CancellationToken.None);
        Assert.Equal(expected, people[0].DisplayName);
    }

    [Fact]
    public async Task Unknown_and_blank_names_return_empty_and_top_is_respected()
    {
        Assert.Empty(await TestHost.People.ResolveAsync("Zebediah Quux", 5, CancellationToken.None));
        Assert.Empty(await TestHost.People.ResolveAsync("  ", 5, CancellationToken.None));
        Assert.Single(await TestHost.People.ResolveAsync("Priya", 1, CancellationToken.None));
    }
}

public class SearchConnectorTests
{
    private static readonly string[] AllTypes = [SearchEntityTypes.Message, SearchEntityTypes.DriveItem, SearchEntityTypes.ChatMessage, SearchEntityTypes.Event];
    private static readonly DateRange NoRange = new(null, null);

    [Fact]
    public async Task Merges_all_entity_types_by_rank()
    {
        var res = await TestHost.Search.SearchAsync(new SearchRequest("Q3 budget review", AllTypes, NoRange, 30), CancellationToken.None);
        Assert.True(res.Total >= res.Hits.Count);
        Assert.True(res.Hits.Zip(res.Hits.Skip(1)).All(p => p.First.Rank >= p.Second.Rank));
        var kinds = res.Hits.Select(h => h.EntityType).ToHashSet();
        Assert.Contains(SearchEntityTypes.Message, kinds);
        Assert.Contains(SearchEntityTypes.DriveItem, kinds);
        Assert.Contains(SearchEntityTypes.ChatMessage, kinds);
        Assert.Contains(SearchEntityTypes.Event, kinds);
        Assert.All(res.Hits, h => Assert.Equal(1, new object?[] { h.Message, h.DriveItem, h.ChatMessage, h.Event }.Count(x => x is not null)));
    }

    [Fact]
    public async Task Entity_types_restrict_results_and_top_limits()
    {
        var res = await TestHost.Search.SearchAsync(new SearchRequest("atlas", [SearchEntityTypes.Event], NoRange, 2), CancellationToken.None);
        Assert.All(res.Hits, h => Assert.Equal(SearchEntityTypes.Event, h.EntityType));
        Assert.True(res.Hits.Count <= 2);
        Assert.True(res.Total >= res.Hits.Count);
    }

    [Fact]
    public async Task Range_filter_applies_and_empty_types_means_all()
    {
        var res = await TestHost.Search.SearchAsync(
            new SearchRequest("", [], new DateRange(TestHost.Day(11, 10), TestHost.Day(11, 12)), 50), CancellationToken.None);
        Assert.NotEmpty(res.Hits);
        Assert.True(res.Hits.Select(h => h.EntityType).Distinct().Count() >= 2);
    }

    [Fact]
    public async Task Postmortem_query_finds_the_postmortem_doc()
    {
        var res = await TestHost.Search.SearchAsync(new SearchRequest("auth outage postmortem", [SearchEntityTypes.DriveItem], NoRange, 3), CancellationToken.None);
        Assert.Equal("Auth-Outage-Postmortem-2026-10-14.docx", res.Hits[0].DriveItem!.Name);
    }
}

public class BehaviorTests
{
    [Fact]
    public async Task Latency_option_delays_calls_and_overrides_apply()
    {
        using var sp = TestHost.Create(new()
        {
            ["Mock:SimulatedLatencyMs"] = "150",
            ["Mock:ConnectorLatencyMs:Teams"] = "0",
        });
        var mail = sp.GetRequiredService<IMailConnector>();
        var teams = sp.GetRequiredService<ITeamsConnector>();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await mail.SearchAsync(new MailSearchRequest(), CancellationToken.None);
        Assert.True(sw.ElapsedMilliseconds >= 120, $"mail took {sw.ElapsedMilliseconds}ms");

        sw.Restart();
        await teams.SearchAsync(new TeamsSearchRequest(), CancellationToken.None);
        Assert.True(sw.ElapsedMilliseconds < 100, $"teams took {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task Latency_is_cancellable()
    {
        using var sp = TestHost.Create(new() { ["Mock:SimulatedLatencyMs"] = "10000" });
        using var cts = new CancellationTokenSource(50);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sp.GetRequiredService<IFilesConnector>().SearchAsync(new FileSearchRequest(), cts.Token));
    }

    [Fact]
    public async Task Already_cancelled_token_is_honored_by_all_connectors()
    {
        var sp = TestHost.Default;
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var ct = cts.Token;
        var none = new DateRange(null, null);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sp.GetRequiredService<IMailConnector>().SearchAsync(new MailSearchRequest(), ct));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sp.GetRequiredService<IMailConnector>().GetAsync("x", ct));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sp.GetRequiredService<IMailConnector>().ListThreadAsync("x", ct));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sp.GetRequiredService<ITeamsConnector>().SearchAsync(new TeamsSearchRequest(), ct));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sp.GetRequiredService<IFilesConnector>().GetContentAsync("d", "i", ct));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sp.GetRequiredService<ICopilotChatConnector>().SearchAsync(new CopilotChatQuery(), ct));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sp.GetRequiredService<IPeopleConnector>().ResolveAsync("a", 1, ct));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sp.GetRequiredService<IPeopleConnector>().GetMeAsync(ct));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sp.GetRequiredService<ISearchConnector>().SearchAsync(new SearchRequest("a", [], none, 1), ct));
    }

    [Fact]
    public void Default_mode_registers_mock_singletons()
    {
        var sp = TestHost.Create();
        Assert.IsType<MockMailConnector>(sp.GetRequiredService<IMailConnector>());
        Assert.IsType<MockSearchConnector>(sp.GetRequiredService<ISearchConnector>());
        Assert.Same(sp.GetRequiredService<IMailConnector>(), sp.GetRequiredService<IMailConnector>());
        Assert.Same(sp.GetRequiredService<FixtureStore>(), sp.GetRequiredService<FixtureStore>());
    }

    [Fact]
    public void AddMockConnectors_works_without_configuration()
    {
        using var sp = new ServiceCollection().AddMockConnectors().BuildServiceProvider();
        Assert.IsType<MockPeopleConnector>(sp.GetRequiredService<IPeopleConnector>());
        Assert.IsType<MockFilesConnector>(sp.GetRequiredService<IFilesConnector>());
        Assert.IsType<MockTeamsConnector>(sp.GetRequiredService<ITeamsConnector>());
        Assert.IsType<MockCopilotChatConnector>(sp.GetRequiredService<ICopilotChatConnector>());
    }

    [Fact]
    public async Task Graph_mode_registers_stubs_that_throw()
    {
        using var sp = TestHost.Create(new() { ["Connectors:Mode"] = "Graph" });
        Assert.IsType<GraphMailConnector>(sp.GetRequiredService<IMailConnector>());
        Assert.IsType<GraphSearchConnector>(sp.GetRequiredService<ISearchConnector>());
        Assert.IsType<GraphPeopleConnector>(sp.GetRequiredService<IPeopleConnector>());
        Assert.IsType<GraphTeamsConnector>(sp.GetRequiredService<ITeamsConnector>());
        Assert.IsType<GraphFilesConnector>(sp.GetRequiredService<IFilesConnector>());
        Assert.IsType<GraphCopilotChatConnector>(sp.GetRequiredService<ICopilotChatConnector>());
        var ex = await Assert.ThrowsAsync<NotImplementedException>(() => sp.GetRequiredService<IMailConnector>().GetAsync("x", CancellationToken.None));
        Assert.Equal("Graph connectors are P1", ex.Message);
    }

    [Fact]
    public void Unknown_mode_is_rejected()
    {
        Assert.Throws<InvalidOperationException>(() => TestHost.Create(new() { ["Connectors:Mode"] = "Bogus" }));
    }
}
