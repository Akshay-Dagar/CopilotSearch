using CopilotSearch.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CopilotSearch.Connectors;

public static class ConnectorServiceCollectionExtensions
{
    public const string ModeKey = "Connectors:Mode";

    /// <summary>Registers the fixture-backed connectors. Settings come from the "Mock" section when configuration is given.</summary>
    public static IServiceCollection AddMockConnectors(this IServiceCollection services, IConfiguration? configuration = null)
    {
        if (configuration is null)
        {
            services.AddOptions<MockOptions>();
        }
        else
        {
            services.Configure<MockOptions>(configuration.GetSection(MockOptions.SectionName));
        }

        services.AddSingleton<FixtureStore>();
        services.AddSingleton<ISearchConnector, MockSearchConnector>();
        services.AddSingleton<IPeopleConnector, MockPeopleConnector>();
        services.AddSingleton<IMailConnector, MockMailConnector>();
        services.AddSingleton<ITeamsConnector, MockTeamsConnector>();
        services.AddSingleton<IFilesConnector, MockFilesConnector>();
        services.AddSingleton<ICopilotChatConnector, MockCopilotChatConnector>();
        return services;
    }

    /// <summary>Registers connectors for <c>Connectors:Mode</c> = Mock (default) or Graph.</summary>
    public static IServiceCollection AddConnectors(this IServiceCollection services, IConfiguration configuration)
    {
        var mode = configuration[ModeKey];
        if (string.IsNullOrWhiteSpace(mode) || mode.Equals("Mock", StringComparison.OrdinalIgnoreCase))
        {
            return services.AddMockConnectors(configuration);
        }

        if (!mode.Equals("Graph", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unknown {ModeKey} '{mode}'. Expected Mock or Graph.");
        }

        services.AddSingleton<ISearchConnector, GraphSearchConnector>();
        services.AddSingleton<IPeopleConnector, GraphPeopleConnector>();
        services.AddSingleton<IMailConnector, GraphMailConnector>();
        services.AddSingleton<ITeamsConnector, GraphTeamsConnector>();
        services.AddSingleton<IFilesConnector, GraphFilesConnector>();
        services.AddSingleton<ICopilotChatConnector, GraphCopilotChatConnector>();
        return services;
    }
}
