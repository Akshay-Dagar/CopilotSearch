namespace CopilotSearch.Orchestration;

/// <summary>Bound to configuration section "Orchestration".</summary>
public sealed class OrchestrationOptions
{
    public const string SectionName = "Orchestration";

    /// <summary>Overall wall-clock budget for one search run.</summary>
    public int BudgetSeconds { get; set; } = 90;
    /// <summary>Max serialized characters of a single tool result passed back to the LLM (~4 chars/token).</summary>
    public int MaxToolOutputChars { get; set; } = 32000;
    public int MaxResultsCap { get; set; } = 25;
}
