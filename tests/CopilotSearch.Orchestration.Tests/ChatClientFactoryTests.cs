using CopilotSearch.Llm;
using Microsoft.Extensions.Options;

namespace CopilotSearch.Orchestration.Tests;

public class ChatClientFactoryTests
{
    private static ChatClientFactory Create(Action<LlmOptions>? configure = null)
    {
        var o = new LlmOptions
        {
            AzureOpenAI = { Endpoint = "https://example.openai.azure.com/", ApiKey = "k", Deployment = "gpt-6-luna" },
            Claude = { ResourceName = "my-foundry", ApiKey = "k", Model = "claude-sonnet-5.5" },
        };
        configure?.Invoke(o);
        return new ChatClientFactory(Options.Create(o));
    }

    [Fact]
    public void UsesConfiguredProvider_ByDefault()
    {
        var client = Create().Create();
        Assert.Equal(LlmProviders.AzureOpenAI, client.Provider);
        Assert.Equal("gpt-6-luna", client.Model);
    }

    [Fact]
    public void ClaudeProvider_CanBeSelectedByConfigOrOverride()
    {
        var byConfig = Create(o => o.Provider = LlmProviders.Claude).Create();
        var byOverride = Create().Create("claude");
        Assert.All([byConfig, byOverride], c =>
        {
            Assert.Equal(LlmProviders.Claude, c.Provider);
            Assert.Equal("claude-sonnet-5.5", c.Model);
            Assert.NotNull(c.Client);
        });
    }

    [Fact]
    public void DefaultAzureCredentialMode_DoesNotRequireApiKey()
    {
        var factory = Create(o =>
        {
            o.AzureOpenAI.AuthMode = LlmAuthModes.DefaultAzureCredential;
            o.AzureOpenAI.ApiKey = "";
            o.Claude.AuthMode = LlmAuthModes.DefaultAzureCredential;
            o.Claude.ApiKey = "";
        });
        Assert.NotNull(factory.Create(LlmProviders.AzureOpenAI).Client);
        Assert.NotNull(factory.Create(LlmProviders.Claude).Client);
    }

    [Theory]
    [InlineData("Llm:AzureOpenAI:Endpoint")]
    [InlineData("Llm:AzureOpenAI:ApiKey")]
    public void MissingAzureOpenAISettings_ThrowsNamingTheKey(string key)
    {
        var factory = Create(o =>
        {
            if (key.EndsWith("Endpoint")) o.AzureOpenAI.Endpoint = "";
            else o.AzureOpenAI.ApiKey = "";
        });
        var ex = Assert.Throws<LlmConfigurationException>(() => factory.Create());
        Assert.Contains(key, ex.Message);
    }

    [Theory]
    [InlineData("Llm:Claude:ResourceName")]
    [InlineData("Llm:Claude:ApiKey")]
    public void MissingClaudeSettings_ThrowsNamingTheKey(string key)
    {
        var factory = Create(o =>
        {
            if (key.EndsWith("ResourceName")) o.Claude.ResourceName = "";
            else o.Claude.ApiKey = "";
        });
        var ex = Assert.Throws<LlmConfigurationException>(() => factory.Create("Claude"));
        Assert.Contains(key, ex.Message);
    }

    [Fact]
    public void UnknownProvider_Throws() =>
        Assert.Throws<UnknownProviderException>(() => Create().Create("gemini"));
}
