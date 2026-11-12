using System.Globalization;
using CopilotSearch.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CopilotSearch.Tools;

/// <summary>Thrown by tool bodies to produce an invalid_argument envelope.</summary>
internal sealed class ToolArgumentException(string message) : Exception(message);

/// <summary>Thrown by tool bodies to produce a not_found envelope.</summary>
internal sealed class ToolNotFoundException(string message) : Exception(message);

/// <summary>Shared plumbing: timeout, error translation, argument parsing and clamping.</summary>
public abstract class ToolBase
{
    private readonly ILogger _logger;
    private readonly IUserContext _userContext;

    protected ToolBase(IOptions<ToolOptions> options, IUserContext userContext, ILogger logger)
    {
        Options = options.Value;
        _userContext = userContext;
        _logger = logger;
    }

    protected ToolOptions Options { get; }

    /// <summary>Runs a tool body under the per-tool timeout and converts failures into error envelopes.</summary>
    internal async Task<object> RunAsync(string toolName, CancellationToken ct, Func<CancellationToken, Task<object>> body)
    {
        var timeout = Options.TimeoutFor(toolName);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            // WaitAsync also enforces the timeout against connectors that ignore the token.
            return await body(cts.Token).WaitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Tool {Tool} timed out after {Timeout}", toolName, timeout);
            return ErrorEnvelope.Create(ToolErrorCodes.Timeout,
                $"Tool '{toolName}' timed out after {timeout.TotalSeconds:0.##}s. Retry, or narrow the query.", retryable: true);
        }
        catch (ToolArgumentException ex)
        {
            return ErrorEnvelope.Create(ToolErrorCodes.InvalidArgument, ex.Message);
        }
        catch (ToolNotFoundException ex)
        {
            return ErrorEnvelope.Create(ToolErrorCodes.NotFound, ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Tool {Tool} failed", toolName);
            return ErrorEnvelope.Create(ToolErrorCodes.UpstreamError, $"Tool '{toolName}' failed: {ex.Message}");
        }
    }

    protected int ClampTop(int? top, int? defaultTop = null, int? maxTop = null)
    {
        var max = maxTop ?? Options.MaxTop;
        var def = Math.Min(defaultTop ?? Options.DefaultTop, max);
        return Math.Clamp(top ?? def, 1, max);
    }

    protected static int ClampChars(int? value, int defaultValue) =>
        Math.Clamp(value ?? defaultValue, 1, MaxCharsLimit);

    internal const int MaxCharsLimit = 100_000;

    /// <summary>
    /// Parses an ISO-8601 value. Date-only input is interpreted in the user's time zone: start of day, or
    /// 23:59:59.999 of that day when <paramref name="endOfDay"/> is set. Full timestamps are taken as-is (UTC if no offset).
    /// </summary>
    protected DateTimeOffset? ParseDate(string? value, string parameter, bool endOfDay = false)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            var local = endOfDay
                ? date.ToDateTime(new TimeOnly(23, 59, 59, 999))
                : date.ToDateTime(TimeOnly.MinValue);
            return new DateTimeOffset(local, UserZone().GetUtcOffset(local)).ToUniversalTime();
        }
        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces, out var result))
            return result.ToUniversalTime();
        throw new ToolArgumentException($"'{parameter}' must be an ISO-8601 date/time such as 2026-11-05T00:00:00Z or 2026-11-05, got '{value}'.");
    }

    protected DateRange ParseRange(string? start, string? end)
    {
        var s = ParseDate(start, "start");
        var e = ParseDate(end, "end", endOfDay: true);
        if (s > e) throw new ToolArgumentException("'start' must not be later than 'end'.");
        return new DateRange(s, e);
    }

    private TimeZoneInfo UserZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(_userContext.TimeZone); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { return TimeZoneInfo.Utc; }
    }

    /// <summary>Case-insensitive enum check; returns the canonical lowercase value or the default when omitted.</summary>
    protected static string ParseEnum(string? value, string parameter, string defaultValue, params string[] allowed)
    {
        if (string.IsNullOrWhiteSpace(value)) return defaultValue;
        var normalized = value.Trim().ToLowerInvariant();
        if (allowed.Contains(normalized)) return normalized;
        throw new ToolArgumentException($"'{parameter}' must be one of: {string.Join(", ", allowed)}. Got '{value}'.");
    }

    protected static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    protected static string DescribeQuery(params (string Name, object? Value)[] parts) =>
        string.Join(' ', parts.Where(p => p.Value is not null).Select(p => $"{p.Name}={FormatValue(p.Value!)}"));

    private static string FormatValue(object value) => value switch
    {
        DateTimeOffset d => d.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.FFF'Z'", CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };
}
