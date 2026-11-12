using CopilotSearch.Llm;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CopilotSearch.Orchestration;

public static class OrchestrationServiceCollectionExtensions
{
    /// <summary>Requires IToolProvider (AddSearchTools) to be registered separately.</summary>
    public static IServiceCollection AddOrchestration(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddLlm(configuration);
        services.Configure<OrchestrationOptions>(configuration.GetSection(OrchestrationOptions.SectionName));
        services.AddSingleton<ISearchOrchestrator, SearchOrchestrator>();
        return services;
    }
}
