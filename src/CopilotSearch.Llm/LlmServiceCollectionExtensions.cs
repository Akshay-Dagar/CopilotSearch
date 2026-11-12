using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CopilotSearch.Llm;

public static class LlmServiceCollectionExtensions
{
    public static IServiceCollection AddLlm(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<LlmOptions>(configuration.GetSection(LlmOptions.SectionName));
        services.AddSingleton<IChatClientFactory, ChatClientFactory>();
        return services;
    }
}
