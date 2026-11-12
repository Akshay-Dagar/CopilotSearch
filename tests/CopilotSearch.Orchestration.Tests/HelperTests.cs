using CopilotSearch.Contracts;
using System.Text.Json.Nodes;

namespace CopilotSearch.Orchestration.Tests;

public class CitationProcessorTests
{
    private static ResultRegistry Registry(params string[] ids)
    {
        var r = new ResultRegistry();
        foreach (var id in ids)
            r.Add(new ResultItem { Id = id, Source = "mail", Type = "email", Title = $"T {id}", Url = $"https://x/{id}" });
        return r;
    }

    [Fact]
    public void NumbersCitationsInOrderOfFirstUse_AndReusesNumbers()
    {
        var result = CitationProcessor.Process("A [mail:b]. B [mail:a]. C [mail:b].", Registry("mail:a", "mail:b"));
        Assert.Equal("A [1]. B [2]. C [1].", result.Answer);
        Assert.Equal(["mail:b", "mail:a"], result.Citations.Select(c => c.Id).ToArray());
    }

    [Fact]
    public void DropsUnknownIds()
    {
        var result = CitationProcessor.Process("Claim [mail:nope] here.", Registry("mail:a"));
        Assert.Equal("Claim here.", result.Answer);
        Assert.Empty(result.Citations);
    }

    [Fact]
    public void HandlesCommaSeparatedGroups()
    {
        var result = CitationProcessor.Process("X [mail:a, mail:b, mail:zzz].", Registry("mail:a", "mail:b"));
        Assert.Equal("X [1][2].", result.Answer);
    }

    [Fact]
    public void LeavesOrdinaryBracketsAlone()
    {
        var result = CitationProcessor.Process("See [draft] and [1] and [note: this].", Registry("mail:a"));
        Assert.Equal("See [draft] and [1] and [note: this].", result.Answer);
    }
}

public class ToolOutputTruncatorTests
{
    [Fact]
    public void SmallOutput_IsUnchanged()
    {
        var node = JsonNode.Parse("""{"results":[{"id":"a"}]}""")!;
        Assert.Equal("""{"results":[{"id":"a"}]}""", ToolOutputTruncator.Truncate(node, 1000).ToJsonString());
    }

    [Fact]
    public void SearchEnvelope_DropsTrailingResults_AndFlagsTruncation()
    {
        var results = new JsonArray();
        for (var i = 0; i < 20; i++) results.Add(new JsonObject { ["id"] = $"mail:{i}", ["snippet"] = new string('x', 100) });
        var node = new JsonObject { ["results"] = results, ["returned"] = 20 };

        var truncated = (JsonObject)ToolOutputTruncator.Truncate(node, 1500);

        Assert.True(truncated["truncated"]!.GetValue<bool>());
        var kept = truncated["results"]!.AsArray().Count;
        Assert.InRange(kept, 1, 19);
        Assert.Equal(kept, truncated["returned"]!.GetValue<int>());
        Assert.Equal("mail:0", truncated["results"]![0]!["id"]!.GetValue<string>());
        Assert.Single(truncated["warnings"]!.AsArray());
    }

    [Fact]
    public void NonSearchOutput_IsCutWithMarker()
    {
        var node = new JsonObject { ["text"] = new string('y', 5000) };
        var truncated = (JsonObject)ToolOutputTruncator.Truncate(node, 1000);
        Assert.True(truncated["truncated"]!.GetValue<bool>());
        Assert.True(truncated["content"]!.GetValue<string>().Length <= 1000);
    }
}

public class ResultRegistryTests
{
    [Fact]
    public void KeepsHighestScoredDuplicate_AndOrdersByScore()
    {
        var r = new ResultRegistry();
        r.Add(new ResultItem { Id = "a", Source = "mail", Type = "email", Title = "low", Score = 0.1 });
        r.Add(new ResultItem { Id = "a", Source = "mail", Type = "email", Title = "high", Score = 0.9 });
        r.Add(new ResultItem { Id = "b", Source = "mail", Type = "email", Title = "mid", Score = 0.5 });

        Assert.Equal(2, r.Count);
        Assert.Equal(["high", "mid"], r.All().Select(i => i.Title).ToArray());
    }
}
