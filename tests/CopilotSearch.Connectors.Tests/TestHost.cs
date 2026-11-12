using CopilotSearch.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CopilotSearch.Connectors.Tests;

internal static class TestHost
{
    public static ServiceProvider Create(Dictionary<string, string?>? settings = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings ?? []).Build();
        return new ServiceCollection().AddConnectors(configuration).BuildServiceProvider();
    }

    public static readonly ServiceProvider Default = Create();

    public static FixtureStore Fixtures => Default.GetRequiredService<FixtureStore>();
    public static IMailConnector Mail => Default.GetRequiredService<IMailConnector>();
    public static ITeamsConnector Teams => Default.GetRequiredService<ITeamsConnector>();
    public static IFilesConnector Files => Default.GetRequiredService<IFilesConnector>();
    public static ICopilotChatConnector Copilot => Default.GetRequiredService<ICopilotChatConnector>();
    public static IPeopleConnector People => Default.GetRequiredService<IPeopleConnector>();
    public static ISearchConnector Search => Default.GetRequiredService<ISearchConnector>();

    public static DateTimeOffset Day(int month, int day) => new(2026, month, day, 0, 0, 0, TimeSpan.Zero);
}
