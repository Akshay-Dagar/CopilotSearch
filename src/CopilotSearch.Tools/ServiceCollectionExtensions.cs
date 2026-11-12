using CopilotSearch.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CopilotSearch.Tools;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the search tools and <see cref="IToolProvider"/>. Connectors (IPeopleConnector, ISearchConnector,
    /// IMailConnector, ITeamsConnector, IFilesConnector, ICopilotChatConnector) must be registered by the caller
    /// with a singleton or transient lifetime.
    /// </summary>
    public static IServiceCollection AddSearchTools(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ToolOptions>().Bind(configuration.GetSection(ToolOptions.SectionName));
        services.AddLogging();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IUserContext, DemoUserContext>();

        services.TryAddSingleton<IdentityTools>();
        services.TryAddSingleton<SearchAllTool>();
        services.TryAddSingleton<MailTools>();
        services.TryAddSingleton<TeamsTools>();
        services.TryAddSingleton<FilesTools>();
        services.TryAddSingleton<CopilotChatTools>();
        services.TryAddSingleton<IToolProvider, ToolProvider>();
        return services;
    }
}
