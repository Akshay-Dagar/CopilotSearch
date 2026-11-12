using CopilotSearch.Connectors;
using CopilotSearch.Contracts;
using CopilotSearch.Tools;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;

// Usage: CopilotSearch.Mcp [--stdio | --http [--urls http://localhost:5173]]
var useHttp = args.Contains("--http", StringComparer.OrdinalIgnoreCase);
var contentRoot = AppContext.BaseDirectory;

if (useHttp)
{
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, ContentRootPath = contentRoot });
    if (string.IsNullOrEmpty(builder.Configuration["urls"]))
        builder.WebHost.UseUrls("http://localhost:5173");

    AddServices(builder.Services, builder.Configuration)
        .WithHttpTransport();

    var app = builder.Build();
    app.MapMcp("/mcp");
    await app.RunAsync();
}
else
{
    var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = args, ContentRootPath = contentRoot });
    // stdout carries the MCP protocol, so every log line must go to stderr.
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

    AddServices(builder.Services, builder.Configuration)
        .WithStdioServerTransport();

    await builder.Build().RunAsync();
}

static IMcpServerBuilder AddServices(IServiceCollection services, IConfiguration configuration)
{
    services.AddMockConnectors(configuration);
    services.AddSearchTools(configuration);

    // Each MCP tool wraps the AIFunction from IToolProvider, so tool logic lives only in CopilotSearch.Tools.
    foreach (var name in ToolNames.All)
    {
        services.AddSingleton(sp => McpServerTool.Create(
            sp.GetRequiredService<IToolProvider>().GetTools().OfType<AIFunction>().Single(t => t.Name == name)));
    }

    return services.AddMcpServer(o => o.ServerInfo = new() { Name = "copilot-search", Version = "0.1.0" });
}
