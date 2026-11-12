using Anthropic;
using Anthropic.Foundry;
using Azure;
using Azure.AI.OpenAI;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;

namespace CopilotSearch.Llm;

/// <summary>A chat client plus the provider/model it talks to.</summary>
public sealed record LlmClient(IChatClient Client, string Provider, string Model);

public interface IChatClientFactory
{
    /// <summary>Creates a client for the configured provider, or <paramref name="providerOverride"/> when set.</summary>
    LlmClient Create(string? providerOverride = null);
}

public sealed class UnknownProviderException(string provider)
    : Exception($"Unknown LLM provider '{provider}'. Expected '{LlmProviders.AzureOpenAI}' or '{LlmProviders.Claude}'.");

public sealed class LlmConfigurationException(string message) : Exception(message);

public sealed class ChatClientFactory(IOptions<LlmOptions> options) : IChatClientFactory
{
    private const string FoundryScope = "https://ai.azure.com/.default";
    private const string CognitiveServicesScope = "https://cognitiveservices.azure.com/.default";

    private readonly LlmOptions _options = options.Value;

    public LlmClient Create(string? providerOverride = null)
    {
        var provider = string.IsNullOrWhiteSpace(providerOverride) ? _options.Provider : providerOverride.Trim();
        if (provider.Equals(LlmProviders.AzureOpenAI, StringComparison.OrdinalIgnoreCase))
            return CreateAzureOpenAI();
        if (provider.Equals(LlmProviders.Claude, StringComparison.OrdinalIgnoreCase))
            return CreateClaude();
        if (provider.Equals(LlmProviders.Demo, StringComparison.OrdinalIgnoreCase))
            return new LlmClient(new DemoChatClient(), LlmProviders.Demo, "demo-heuristic");
        throw new UnknownProviderException(provider);
    }

    private LlmClient CreateAzureOpenAI()
    {
        var o = _options.AzureOpenAI;
        Require(o.Endpoint, "Llm:AzureOpenAI:Endpoint");
        Require(o.Deployment, "Llm:AzureOpenAI:Deployment");

        var endpoint = new Uri(o.Endpoint);
        AzureOpenAIClient client = UsesApiKey(o.AuthMode)
            ? new AzureOpenAIClient(endpoint, new AzureKeyCredential(Require(o.ApiKey, "Llm:AzureOpenAI:ApiKey")))
            : new AzureOpenAIClient(endpoint, new DefaultAzureCredential());

        return new LlmClient(client.GetChatClient(o.Deployment).AsIChatClient(), LlmProviders.AzureOpenAI, o.Deployment);
    }

    private LlmClient CreateClaude()
    {
        var o = _options.Claude;
        Require(o.ResourceName, "Llm:Claude:ResourceName");
        Require(o.Model, "Llm:Claude:Model");

        IAnthropicFoundryCredentials credentials = UsesApiKey(o.AuthMode)
            ? new AnthropicFoundryApiKeyCredentials(Require(o.ApiKey, "Llm:Claude:ApiKey"), o.ResourceName)
            : new AnthropicFoundryIdentityTokenCredentials(new DefaultAzureCredential(), o.ResourceName, [FoundryScope]);

        IAnthropicClient client = new AnthropicFoundryClient(credentials);
        return new LlmClient(client.AsIChatClient(o.Model, o.MaxTokens), LlmProviders.Claude, o.Model);
    }

    private static bool UsesApiKey(string authMode) =>
        !authMode.Equals(LlmAuthModes.DefaultAzureCredential, StringComparison.OrdinalIgnoreCase);

    private static string Require(string value, string key) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new LlmConfigurationException($"Missing configuration value '{key}'. Set it in appsettings, user-secrets or environment variables.")
            : value;
}
