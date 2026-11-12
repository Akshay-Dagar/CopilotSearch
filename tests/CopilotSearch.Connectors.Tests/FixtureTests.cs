using CopilotSearch.Contracts;

namespace CopilotSearch.Connectors.Tests;

public class FixtureTests
{
    private static readonly FixtureStore F = TestHost.Fixtures;
    private static readonly DateTimeOffset WindowStart = new(2026, 9, 17, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WindowEnd = new(2026, 11, 12, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Counts_match_design()
    {
        Assert.Equal(15, F.Users.Count);
        Assert.InRange(F.Messages.Count, 40, 45);
        Assert.InRange(F.ChatMessages.Count, 60, 70);
        Assert.InRange(F.DriveItems.Count, 25, 30);
        Assert.InRange(F.Interactions.Count, 20, 24);
        Assert.InRange(F.Events.Count, 10, 12);
        Assert.Equal(F.DriveItems.Count, F.FileTexts.Count);
    }

    [Fact]
    public void Ids_are_unique()
    {
        Assert.Equal(F.Users.Count, F.Users.Select(x => x.Id).Distinct().Count());
        Assert.Equal(F.Messages.Count, F.Messages.Select(x => x.Id).Distinct().Count());
        Assert.Equal(F.ChatMessages.Count, F.ChatMessages.Select(x => x.Id).Distinct().Count());
        Assert.Equal(F.DriveItems.Count, F.DriveItems.Select(x => (x.DriveId, x.Id)).Distinct().Count());
        Assert.Equal(F.Interactions.Count, F.Interactions.Select(x => x.Id).Distinct().Count());
        Assert.Equal(F.Events.Count, F.Events.Select(x => x.Id).Distinct().Count());
    }

    [Fact]
    public void Demo_user_is_configured()
    {
        var me = F.Users.Single(u => u.Id == "u-akshay");
        Assert.Equal("Akshay Dagar", me.DisplayName);
        Assert.Equal("akshay@contoso.com", me.Mail);
        Assert.Equal("Asia/Kolkata", me.TimeZone);
        Assert.NotNull(me.Manager);
    }

    [Fact]
    public void Referenced_contoso_people_exist()
    {
        var known = F.Users.Select(u => u.Mail).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var referenced = new List<GraphEmailAddress>();
        foreach (var m in F.Messages)
        {
            referenced.Add(m.From);
            referenced.AddRange(m.ToRecipients);
            referenced.AddRange(m.CcRecipients);
        }

        foreach (var c in F.ChatMessages)
        {
            referenced.Add(c.From);
            referenced.AddRange(c.Mentions);
        }

        foreach (var d in F.DriveItems)
        {
            referenced.AddRange(new[] { d.CreatedBy, d.LastModifiedBy }.OfType<GraphEmailAddress>());
        }

        foreach (var e in F.Events)
        {
            referenced.AddRange(e.Attendees);
            referenced.AddRange(new[] { e.Organizer }.OfType<GraphEmailAddress>());
        }

        var missing = referenced
            .Where(a => a.Address.EndsWith("@contoso.com", StringComparison.OrdinalIgnoreCase) && !known.Contains(a.Address))
            .Select(a => a.Address)
            .Distinct();
        Assert.Empty(missing);
    }

    [Fact]
    public void Dates_fall_in_the_eight_week_window()
    {
        var dates = F.Messages.Select(m => m.ReceivedDateTime)
            .Concat(F.ChatMessages.Select(c => c.CreatedDateTime))
            .Concat(F.DriveItems.SelectMany(d => new[] { d.CreatedDateTime, d.LastModifiedDateTime }))
            .Concat(F.Interactions.Select(i => i.CreatedDateTime))
            .Concat(F.Events.Select(e => e.Start))
            .ToList();
        Assert.All(dates, d => Assert.InRange(d, WindowStart, WindowEnd));
    }

    [Fact]
    public void Several_items_in_last_seven_days()
    {
        var cutoff = WindowEnd.AddDays(-7);
        Assert.True(F.Messages.Count(m => m.ReceivedDateTime >= cutoff) >= 5);
        Assert.True(F.ChatMessages.Count(m => m.CreatedDateTime >= cutoff) >= 5);
        Assert.True(F.DriveItems.Count(d => d.LastModifiedDateTime >= cutoff) >= 3);
    }

    [Fact]
    public void Edge_case_mails_exist()
    {
        Assert.Contains(F.Messages, m => !m.IsRead && m.Importance == "high" && m.HasAttachments && m.Attachments.Count > 0);
        Assert.Contains(F.Messages, m => m.BodyText.Contains("Ignore all previous instructions", StringComparison.Ordinal));
        Assert.True(F.Messages.Count(m => m.ParentFolder == "sent" && m.From.Address == "akshay@contoso.com") >= 6);
        Assert.Contains(F.Messages, m => m.ParentFolder == "drafts");
        Assert.All(F.Messages.Where(m => m.HasAttachments), m => Assert.NotEmpty(m.Attachments));
    }

    [Fact]
    public void Shared_first_names_exist()
    {
        var priyas = F.Users.Where(u => u.DisplayName.StartsWith("Priya ", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, priyas.Count);
    }

    [Fact]
    public void Threads_have_consistent_reply_counts()
    {
        var byId = F.ChatMessages.ToDictionary(c => c.Id);
        foreach (var reply in F.ChatMessages.Where(c => c.ReplyToId is not null))
        {
            Assert.True(byId.ContainsKey(reply.ReplyToId!));
        }

        foreach (var parent in F.ChatMessages.Where(c => c.ReplyCount > 0))
        {
            Assert.Equal(parent.ReplyCount, F.ChatMessages.Count(c => c.ReplyToId == parent.Id));
        }

        Assert.Contains(F.ChatMessages, c => c.Mentions.Count > 0);
        Assert.Contains(F.ChatMessages, c => c.TeamId is not null);
        Assert.Contains(F.ChatMessages, c => c.ChatId is not null);
    }

    [Fact]
    public void File_texts_include_long_documents()
    {
        Assert.All(F.FileTexts.Values, t => Assert.False(string.IsNullOrWhiteSpace(t)));
        Assert.Contains(F.FileTexts.Values, t => t.Length > 5000);
        Assert.Contains(F.DriveItems, d => d.Location == "onedrive");
        Assert.Contains(F.DriveItems, d => d.Location == "sharepoint");
    }

    [Fact]
    public void Copilot_interactions_come_in_pairs_across_apps()
    {
        foreach (var session in F.Interactions.GroupBy(i => i.SessionId))
        {
            Assert.Equal(["userPrompt", "aiResponse"], session.OrderBy(i => i.CreatedDateTime).Select(i => i.InteractionType));
        }

        Assert.True(F.Interactions.Select(i => i.AppClass).Distinct().Count() >= 5);
    }
}
