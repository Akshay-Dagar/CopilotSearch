using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using CopilotSearch.Contracts;
using CopilotSearch.Llm;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CopilotSearch.Orchestration;

public interface ISearchOrchestrator
{
    IAsyncEnumerable<SearchEvent> RunAsync(SearchRunRequest request, CancellationToken cancellationToken);
}

public sealed class SearchOrchestrator(
    IChatClientFactory chatClientFactory,
    IToolProvider toolProvider,
    IOptions<LlmOptions> llmOptions,
    IOptions<OrchestrationOptions> options,
    ILogger<SearchOrchestrator> logger) : ISearchOrchestrator
{
    private readonly OrchestrationOptions _options = options.Value;

    public async IAsyncEnumerable<SearchEvent> RunAsync(
        SearchRunRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid().ToString("N")[..12];

        LlmClient llm;
        string? failureCode = null, failureMessage = null;
        try
        {
            llm = chatClientFactory.Create(request.Provider);
        }
        catch (UnknownProviderException ex)
        {
            (llm, failureCode, failureMessage) = (null!, RunErrorCodes.InvalidProvider, ex.Message);
        }
        catch (LlmConfigurationException ex)
        {
            (llm, failureCode, failureMessage) = (null!, RunErrorCodes.Configuration, ex.Message);
        }

        if (failureCode is not null)
        {
            yield return new ErrorEvent(failureCode, failureMessage!);
            yield break;
        }

        yield return new RunStartedEvent(runId, llm.Provider, llm.Model);

        var channel = Channel.CreateUnbounded<SearchEvent>(new UnboundedChannelOptions { SingleReader = true });
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(_options.BudgetSeconds));

        var producer = Task.Run(() => ProduceAsync(runId, llm, request, channel.Writer, budget.Token, cancellationToken), CancellationToken.None);

        await foreach (var evt in channel.Reader.ReadAllAsync(CancellationToken.None))
            yield return evt;

        await producer;
    }

    private async Task ProduceAsync(
        string runId, LlmClient llm, SearchRunRequest request, ChannelWriter<SearchEvent> writer,
        CancellationToken budgetToken, CancellationToken callerToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var registry = new ResultRegistry();
            var answer = new StringBuilder();
            long inputTokens = 0, outputTokens = 0;

            var tools = toolProvider.GetTools()
                .Select(t => t is AIFunction f
                    ? (AITool)new InstrumentedFunction(f, registry, writer, _options.MaxToolOutputChars)
                    : t)
                .ToList();

            var client = llm.Client.AsBuilder()
                .UseFunctionInvocation(configure: c =>
                {
                    c.MaximumIterationsPerRequest = llmOptions.Value.MaxIterations;
                    c.AllowConcurrentInvocation = true;
                })
                .Build();

            var agent = new ChatClientAgent(client, new ChatClientAgentOptions
            {
                Name = "search-agent",
                ChatOptions = new ChatOptions
                {
                    Instructions = Prompts.SystemPrompt,
                    Tools = tools,
                    Temperature = llmOptions.Value.Temperature,
                    AllowMultipleToolCalls = true,
                },
            });

            await foreach (var update in agent.RunStreamingAsync(request.Query, cancellationToken: budgetToken))
            {
                foreach (var content in update.Contents)
                {
                    switch (content)
                    {
                        case FunctionResultContent:
                            // Text before a tool round-trip is planning chatter, not part of the final answer.
                            answer.Clear();
                            break;
                        case UsageContent usage:
                            inputTokens += usage.Details.InputTokenCount ?? 0;
                            outputTokens += usage.Details.OutputTokenCount ?? 0;
                            break;
                        case TextContent text when !string.IsNullOrEmpty(text.Text):
                            answer.Append(text.Text);
                            writer.TryWrite(new AnswerDeltaEvent(text.Text));
                            break;
                    }
                }
            }

            var answerText = answer.ToString().Trim();
            if (answerText.Length == 0)
            {
                answerText = await FallbackAnswerAsync(llm.Client, request.Query, registry, budgetToken);
                writer.TryWrite(new AnswerDeltaEvent(answerText));
            }

            var processed = CitationProcessor.Process(answerText, registry);
            var maxResults = Math.Clamp(request.MaxResults, 1, _options.MaxResultsCap);

            writer.TryWrite(new ResultsEvent(OrderResults(registry, processed.Citations, maxResults)));
            writer.TryWrite(new FinalEvent(
                runId, processed.Answer, processed.Citations, new UsageInfo(inputTokens, outputTokens), sw.ElapsedMilliseconds));
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            writer.TryWrite(new ErrorEvent(RunErrorCodes.Cancelled, "The request was cancelled."));
        }
        catch (OperationCanceledException)
        {
            writer.TryWrite(new ErrorEvent(RunErrorCodes.BudgetExceeded,
                $"The search exceeded its {_options.BudgetSeconds}s budget."));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Search run {RunId} failed", runId);
            writer.TryWrite(new ErrorEvent(RunErrorCodes.LlmFailure, ex.Message));
        }
        finally
        {
            writer.TryComplete();
        }
    }

    private static List<ResultItem> OrderResults(
        ResultRegistry registry, IReadOnlyList<CitationInfo> citations, int max)
    {
        var ordered = new List<ResultItem>();
        foreach (var citation in citations)
            if (registry.TryGet(citation.Id, out var item))
                ordered.Add(item);

        var cited = ordered.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
        ordered.AddRange(registry.All().Where(i => !cited.Contains(i.Id)));
        return ordered.Take(max).ToList();
    }

    private static async Task<string> FallbackAnswerAsync(
        IChatClient client, string query, ResultRegistry registry, CancellationToken ct)
    {
        var results = registry.All().Take(15)
            .Select(i => new { i.Id, i.Source, i.Title, i.Snippet, i.Url, i.ModifiedAt });
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, Prompts.FallbackSystemPrompt),
            new(ChatRole.User, $"Question: {query}\n\nResults:\n{JsonSerializer.Serialize(results)}"),
        };
        var response = await client.GetResponseAsync(messages, cancellationToken: ct);
        return response.Text.Trim();
    }
}
