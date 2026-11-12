using CopilotSearch.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CopilotSearch.Tools.Tests;

internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

internal static class T
{
    public static IOptions<ToolOptions> Opts(Action<ToolOptions>? configure = null)
    {
        var o = new ToolOptions();
        configure?.Invoke(o);
        return Options.Create(o);
    }

    public static IdentityTools Identity(FakeConnectors f, DateTimeOffset? now = null, Action<ToolOptions>? cfg = null) =>
        new(f, new DemoUserContext(), new FixedClock(now ?? FakeConnectors.T0), Opts(cfg), NullLogger<IdentityTools>.Instance);

    public static MailTools Mail(FakeConnectors f, Action<ToolOptions>? cfg = null) => new(f, Opts(cfg), new DemoUserContext(), NullLogger<MailTools>.Instance);
    public static TeamsTools Teams(FakeConnectors f, Action<ToolOptions>? cfg = null) => new(f, Opts(cfg), new DemoUserContext(), NullLogger<TeamsTools>.Instance);
    public static FilesTools Files(FakeConnectors f, Action<ToolOptions>? cfg = null) => new(f, Opts(cfg), new DemoUserContext(), NullLogger<FilesTools>.Instance);
    public static CopilotChatTools Chats(FakeConnectors f, Action<ToolOptions>? cfg = null) => new(f, Opts(cfg), new DemoUserContext(), NullLogger<CopilotChatTools>.Instance);
    public static SearchAllTool All(FakeConnectors f, Action<ToolOptions>? cfg = null) => new(f, Opts(cfg), new DemoUserContext(), NullLogger<SearchAllTool>.Instance);

    public static ErrorEnvelope Err(object result, string code)
    {
        var e = Assert.IsType<ErrorEnvelope>(result);
        Assert.Equal(code, e.Error.Code);
        return e;
    }
}
