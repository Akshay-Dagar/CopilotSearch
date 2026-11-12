namespace CopilotSearch.Connectors;

/// <summary>Bound from configuration section "Mock".</summary>
public sealed class MockOptions
{
    public const string SectionName = "Mock";

    /// <summary>Delay applied to every mock connector call unless overridden per connector.</summary>
    public int SimulatedLatencyMs { get; set; }

    /// <summary>Per-connector overrides keyed by Search, People, Mail, Teams, Files or CopilotChat.</summary>
    public Dictionary<string, int> ConnectorLatencyMs { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The "current" time used for recency scoring; fixtures are dated relative to 2026-11-12.</summary>
    public DateTimeOffset ReferenceTime { get; set; } = new(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);

    internal int LatencyFor(string connector)
    {
        foreach (var (key, value) in ConnectorLatencyMs)
        {
            if (string.Equals(key, connector, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        return SimulatedLatencyMs;
    }
}

internal static class MockConnectorNames
{
    public const string Search = "Search";
    public const string People = "People";
    public const string Mail = "Mail";
    public const string Teams = "Teams";
    public const string Files = "Files";
    public const string CopilotChat = "CopilotChat";
}
