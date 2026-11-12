using CopilotSearch.Api;
using CopilotSearch.Connectors;
using CopilotSearch.Contracts;
using CopilotSearch.Llm;
using CopilotSearch.Orchestration;
using CopilotSearch.Tools;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddConnectors(builder.Configuration)
    .AddSearchTools(builder.Configuration)
    .AddOrchestration(builder.Configuration);

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// Active configuration without secrets, to see which provider/tools a deployment runs with.
app.MapGet("/config", (
    IOptions<LlmOptions> llm, IOptions<OrchestrationOptions> orchestration,
    IConfiguration configuration, IToolProvider tools) =>
{
    var o = llm.Value;
    return Results.Json(new
    {
        provider = o.Provider,
        models = new { azure_openai = o.AzureOpenAI.Deployment, claude = o.Claude.Model },
        max_iterations = o.MaxIterations,
        budget_seconds = orchestration.Value.BudgetSeconds,
        connectors_mode = configuration["Connectors:Mode"] ?? "Mock",
        tools = tools.GetTools().Select(t => t.Name),
    }, SearchEndpoints.SseJson);
});

app.MapSearchEndpoints();

app.Run();

public partial class Program;
