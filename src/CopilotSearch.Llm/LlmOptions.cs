namespace CopilotSearch.Llm;

public static class LlmProviders
{
    public const string AzureOpenAI = "AzureOpenAI";
    public const string Claude = "Claude";
    /// <summary>Keyless heuristic client for demos and UI development.</summary>
    public const string Demo = "Demo";
}

public static class LlmAuthModes
{
    public const string ApiKey = "ApiKey";
    public const string DefaultAzureCredential = "DefaultAzureCredential";
}

/// <summary>Bound to configuration section "Llm".</summary>
public sealed class LlmOptions
{
    public const string SectionName = "Llm";

    /// <summary>AzureOpenAI | Claude</summary>
    public string Provider { get; set; } = LlmProviders.AzureOpenAI;
    public AzureOpenAIOptions AzureOpenAI { get; set; } = new();
    public ClaudeOptions Claude { get; set; } = new();
    public int MaxIterations { get; set; } = 6;
    public float Temperature { get; set; } = 0.1f;
}

public sealed class AzureOpenAIOptions
{
    /// <summary>e.g. https://my-resource.openai.azure.com/</summary>
    public string Endpoint { get; set; } = "";
    public string Deployment { get; set; } = "gpt-6-luna";
    /// <summary>ApiKey | DefaultAzureCredential</summary>
    public string AuthMode { get; set; } = LlmAuthModes.ApiKey;
    public string ApiKey { get; set; } = "";
}

public sealed class ClaudeOptions
{
    /// <summary>Azure AI Foundry resource name (the "{resource}" in {resource}.services.ai.azure.com).</summary>
    public string ResourceName { get; set; } = "";
    public string Model { get; set; } = "claude-sonnet-5.5";
    /// <summary>ApiKey | DefaultAzureCredential</summary>
    public string AuthMode { get; set; } = LlmAuthModes.ApiKey;
    public string ApiKey { get; set; } = "";
    public int MaxTokens { get; set; } = 4096;
}
