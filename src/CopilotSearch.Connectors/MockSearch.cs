using System.Text;
using CopilotSearch.Contracts;

namespace CopilotSearch.Connectors;

internal readonly record struct DocText(string Primary, string Body, string Meta = "");

/// <summary>Keyword scoring and shared filter helpers for the mock connectors.</summary>
internal static class MockSearch
{
    private const double PrimaryWeight = 3.0;
    private const double MetaWeight = 1.5;
    private const double BodyWeight = 1.0;
    private const double PrefixFactor = 0.7;
    private const double RecencyWeight = 0.15;
    private const double RecencyHalfLifeDays = 14.0;

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a", "an", "the", "and", "or", "of", "to", "in", "on", "at", "for", "from", "with", "by", "about", "as", "is", "are", "was",
        "were", "be", "been", "it", "its", "this", "that", "these", "those", "what", "which", "who", "whom", "when", "where", "how",
        "did", "do", "does", "me", "my", "i", "we", "our", "you", "your", "he", "she", "they", "them", "his", "her", "any", "all",
        "say", "said", "says", "show", "find", "get", "last", "recent", "recently", "latest", "please", "about", "into", "than", "then",
    };

    public static List<string> Tokenize(string? text)
    {
        var tokens = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return tokens;
        }

        var sb = new StringBuilder();
        foreach (var ch in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
            }
            else if (ch != '\'' && sb.Length > 0)
            {
                Flush(sb, tokens);
            }
        }

        Flush(sb, tokens);
        return tokens;
    }

    private static void Flush(StringBuilder sb, List<string> tokens)
    {
        if (sb.Length == 0)
        {
            return;
        }

        var token = sb.ToString();
        sb.Clear();
        if (!StopWords.Contains(token))
        {
            tokens.Add(Stem(token));
        }
    }

    private static string Stem(string token) =>
        token.Length > 3 && token[^1] == 's' && token[^2] != 's' ? token[..^1] : token;

    /// <summary>Scores and orders items. A blank query keeps every item and orders by date descending.</summary>
    public static List<(T Item, double Score)> Rank<T>(
        IEnumerable<T> items, string? query, Func<T, DocText> text, Func<T, DateTimeOffset> date, DateTimeOffset now)
    {
        var queryTokens = Tokenize(query).Distinct().ToList();
        var scored = new List<(T Item, double Score)>();
        foreach (var item in items)
        {
            var recency = Recency(date(item), now);
            if (queryTokens.Count == 0)
            {
                scored.Add((item, Math.Round(recency, 4)));
                continue;
            }

            var relevance = Relevance(queryTokens, text(item));
            if (relevance > 0)
            {
                scored.Add((item, Math.Round(relevance + RecencyWeight * recency, 4)));
            }
        }

        return scored
            .OrderByDescending(s => s.Score)
            .ThenByDescending(s => date(s.Item))
            .ToList();
    }

    private static double Relevance(List<string> queryTokens, DocText doc)
    {
        var primary = new HashSet<string>(Tokenize(doc.Primary));
        var meta = new HashSet<string>(Tokenize(doc.Meta));
        var body = new HashSet<string>(Tokenize(doc.Body));
        double sum = 0;
        foreach (var token in queryTokens)
        {
            sum += Math.Max(
                Math.Max(Match(token, primary) * PrimaryWeight, Match(token, meta) * MetaWeight),
                Match(token, body) * BodyWeight);
        }

        return sum / (PrimaryWeight * queryTokens.Count);
    }

    private static double Match(string token, HashSet<string> tokens)
    {
        if (tokens.Contains(token))
        {
            return 1.0;
        }

        if (token.Length >= 4)
        {
            foreach (var candidate in tokens)
            {
                if (candidate.StartsWith(token, StringComparison.Ordinal))
                {
                    return PrefixFactor;
                }
            }
        }

        return 0;
    }

    private static double Recency(DateTimeOffset date, DateTimeOffset now)
    {
        var ageDays = Math.Max(0, (now - date).TotalDays);
        return 1.0 / (1.0 + ageDays / RecencyHalfLifeDays);
    }

    public static bool InRange(DateTimeOffset value, DateRange range) =>
        (range.Start is null || value >= range.Start) && (range.End is null || value <= range.End);

    public static bool ContainsIgnoreCase(string? value, string? needle) =>
        value is not null && needle is not null && value.Contains(needle, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the filter is blank or the address matches by name or email (case-insensitive contains).</summary>
    public static bool MatchesPerson(GraphEmailAddress? person, string? filter) =>
        string.IsNullOrWhiteSpace(filter) ||
        (person is not null && (ContainsIgnoreCase(person.Name, filter.Trim()) || ContainsIgnoreCase(person.Address, filter.Trim())));

    public static bool MatchesAnyPerson(IEnumerable<GraphEmailAddress> people, string? filter) =>
        string.IsNullOrWhiteSpace(filter) || people.Any(p => MatchesPerson(p, filter));

    public static bool IsAny(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Equals("any", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("all", StringComparison.OrdinalIgnoreCase);

    public static int NormalizeTop(int top) => top <= 0 ? 10 : top;

    public static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max].TrimEnd() + "...";

    public static async Task SimulateLatencyAsync(MockOptions options, string connector, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var ms = options.LatencyFor(connector);
        if (ms > 0)
        {
            await Task.Delay(ms, ct).ConfigureAwait(false);
        }
    }
}
