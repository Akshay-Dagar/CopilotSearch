using CopilotSearch.Contracts;
using Microsoft.Extensions.Options;

namespace CopilotSearch.Connectors;

public sealed class MockPeopleConnector(FixtureStore fixtures, IOptions<MockOptions> options) : IPeopleConnector
{
    private const string DemoUserId = "u-akshay";
    private readonly MockOptions _options = options.Value;

    public async Task<UserProfile> GetMeAsync(CancellationToken ct)
    {
        await MockSearch.SimulateLatencyAsync(_options, MockConnectorNames.People, ct).ConfigureAwait(false);
        return fixtures.Users.First(u => u.Id == DemoUserId);
    }

    public async Task<IReadOnlyList<Person>> ResolveAsync(string name, int top, CancellationToken ct)
    {
        await MockSearch.SimulateLatencyAsync(_options, MockConnectorNames.People, ct).ConfigureAwait(false);

        var query = (name ?? "").Trim().ToLowerInvariant();
        if (query.Length == 0)
        {
            return [];
        }

        var queryTokens = query.Split([' ', ',', '.', '-', '_'], StringSplitOptions.RemoveEmptyEntries);
        return fixtures.Users
            .Select(u => (User: u, Score: Score(u, query, queryTokens)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.User.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(MockSearch.NormalizeTop(top))
            .Select(x => new Person
            {
                Id = x.User.Id,
                DisplayName = x.User.DisplayName,
                Mail = x.User.Mail,
                UserPrincipalName = x.User.UserPrincipalName,
                JobTitle = x.User.JobTitle,
                Department = x.User.Department,
                Score = Math.Round(x.Score, 3),
            })
            .ToList();
    }

    private static double Score(UserProfile user, string query, string[] queryTokens)
    {
        var displayName = user.DisplayName.ToLowerInvariant();
        var best = 0.0;

        if (displayName == query)
        {
            best = 1.0;
        }
        else if (displayName.StartsWith(query, StringComparison.Ordinal))
        {
            best = 0.95;
        }
        else if (displayName.Contains(query, StringComparison.Ordinal))
        {
            best = 0.8;
        }

        var nameTokens = displayName.Split([' ', '-'], StringSplitOptions.RemoveEmptyEntries);
        var perToken = queryTokens.Select(q => nameTokens.Max(n => TokenScore(q, n))).ToList();
        if (perToken.Count > 0 && perToken.All(s => s > 0))
        {
            best = Math.Max(best, perToken.Average());
        }

        foreach (var address in new[] { user.Mail, user.UserPrincipalName })
        {
            var lower = address.ToLowerInvariant();
            var local = lower.Split('@')[0];
            if (lower == query || local == query)
            {
                best = Math.Max(best, 1.0);
            }
            else if (local.StartsWith(query, StringComparison.Ordinal))
            {
                best = Math.Max(best, 0.85);
            }
            else if (lower.Contains(query, StringComparison.Ordinal))
            {
                best = Math.Max(best, 0.6);
            }
        }

        return best;
    }

    private static double TokenScore(string query, string candidate)
    {
        if (candidate == query)
        {
            return 1.0;
        }

        if (candidate.StartsWith(query, StringComparison.Ordinal))
        {
            return 0.8;
        }

        if (query.Length >= 3 && candidate.Contains(query, StringComparison.Ordinal))
        {
            return 0.5;
        }

        return query.Length >= 4 && candidate.Length >= 4 && EditDistance(query, candidate) <= 1 ? 0.6 : 0;
    }

    private static int EditDistance(string a, string b)
    {
        var previous = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var current = new int[b.Length + 1];
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            previous = current;
        }

        return previous[b.Length];
    }
}
