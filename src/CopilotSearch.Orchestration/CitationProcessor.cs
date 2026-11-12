using System.Text;
using System.Text.RegularExpressions;

namespace CopilotSearch.Orchestration;

/// <summary>
/// Converts the model's id-based citations ("[mail:abc]") into numbered citations ("[1]"),
/// dropping ids that no tool returned so hallucinated links never reach the client.
/// </summary>
public static partial class CitationProcessor
{
    [GeneratedRegex(@"\[\s*([a-z_]+:[^\]\s,]+(?:\s*,\s*[a-z_]+:[^\]\s,]+)*)\s*\]", RegexOptions.CultureInvariant)]
    private static partial Regex CitationPattern();

    public sealed record Result(string Answer, IReadOnlyList<CitationInfo> Citations);

    public static Result Process(string answer, ResultRegistry registry)
    {
        var numbers = new Dictionary<string, int>(StringComparer.Ordinal);
        var citations = new List<CitationInfo>();

        var text = CitationPattern().Replace(answer, match =>
        {
            var sb = new StringBuilder();
            foreach (var id in match.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                if (!registry.TryGet(id, out var item))
                    continue;

                if (!numbers.TryGetValue(id, out var n))
                {
                    n = numbers[id] = citations.Count + 1;
                    citations.Add(new CitationInfo(n, item.Id, item.Title, item.Url, item.Source));
                }
                sb.Append('[').Append(n).Append(']');
            }
            return sb.ToString();
        });

        // Removing unknown markers can leave doubled spaces or a space before punctuation.
        text = Regex.Replace(text, @"[ \t]{2,}", " ");
        text = Regex.Replace(text, @"[ \t]+([.,;:])", "$1");
        return new Result(text.Trim(), citations);
    }
}
