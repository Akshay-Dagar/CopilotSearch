using System.ComponentModel;
using CopilotSearch.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CopilotSearch.Tools;

/// <summary>get_current_user, get_current_datetime and resolve_person.</summary>
public sealed class IdentityTools(
    IPeopleConnector people,
    IUserContext userContext,
    TimeProvider clock,
    IOptions<ToolOptions> options,
    ILogger<IdentityTools> logger) : ToolBase(options, userContext, logger)
{
    private const int DefaultPeople = 5;
    private const int MaxPeople = 10;

    [Description("Returns the signed-in user's profile: id, display_name, email, upn, job_title, department, manager, time_zone (IANA) and locale. Use it to resolve 'me', 'my manager' and the user's time zone.")]
    public Task<object> GetCurrentUser(CancellationToken cancellationToken = default) =>
        RunAsync(ToolNames.GetCurrentUser, cancellationToken, async ct =>
        {
            var me = await people.GetMeAsync(ct);
            return new CurrentUserResult
            {
                Id = me.Id,
                DisplayName = me.DisplayName,
                Email = me.Mail,
                Upn = me.UserPrincipalName,
                JobTitle = me.JobTitle,
                Department = me.Department,
                Manager = me.Manager is null ? null : new ManagerInfo(me.Manager.Name, me.Manager.Address),
                TimeZone = string.IsNullOrWhiteSpace(me.TimeZone) ? userContext.TimeZone : me.TimeZone,
                Locale = me.Locale,
            };
        });

    [Description("Returns the current date and time (utc_now, local_now, timezone, day_of_week) plus week_start (Monday 00:00) and week_end (Sunday 23:59:59) of the current week in that time zone. Call it before converting relative expressions like 'last week' or 'yesterday' into ISO-8601 start/end values.")]
    public Task<object> GetCurrentDatetime(
        [Description("IANA time zone id, e.g. 'Asia/Kolkata' or 'America/Los_Angeles'. Defaults to the signed-in user's time zone.")] string? timezone = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(ToolNames.GetCurrentDatetime, cancellationToken, _ =>
        {
            var zoneId = Blank(timezone) ?? userContext.TimeZone;
            TimeZoneInfo zone;
            try
            {
                zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                throw new ToolArgumentException($"'timezone' must be a valid IANA time zone id such as 'Asia/Kolkata', got '{zoneId}'.");
            }

            var utcNow = clock.GetUtcNow();
            var localNow = TimeZoneInfo.ConvertTime(utcNow, zone);
            var monday = localNow.Date.AddDays(-(((int)localNow.DayOfWeek + 6) % 7));
            var sunday = monday.AddDays(6).AddHours(23).AddMinutes(59).AddSeconds(59);

            return Task.FromResult<object>(new CurrentDatetimeResult
            {
                UtcNow = utcNow,
                LocalNow = localNow,
                Timezone = zoneId,
                DayOfWeek = localNow.DayOfWeek.ToString(),
                WeekStart = new DateTimeOffset(monday, zone.GetUtcOffset(monday)),
                WeekEnd = new DateTimeOffset(sunday, zone.GetUtcOffset(sunday)),
            });
        });

    [Description("Resolves a person's name, alias or partial email to matching people (id, display_name, email, upn, job_title, department, score). Several matches may be returned; pick the best one or ask the user to disambiguate. Always call this before filtering other tools by a person (from, to, author, mentions).")]
    public Task<object> ResolvePerson(
        [Description("Display name, alias or email fragment of the person, e.g. 'Priya' or 'priya.s'.")] string name,
        [Description("Maximum number of matches to return. Default 5, maximum 10.")] int? top = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(ToolNames.ResolvePerson, cancellationToken, async ct =>
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ToolArgumentException("'name' is required.");

            var matches = await people.ResolveAsync(name.Trim(), ClampTop(top, DefaultPeople, MaxPeople), ct);
            return new ResolvePersonResult
            {
                People = matches.Select(p => new PersonInfo
                {
                    Id = p.Id,
                    DisplayName = p.DisplayName,
                    Email = p.Mail,
                    Upn = p.UserPrincipalName,
                    JobTitle = p.JobTitle,
                    Department = p.Department,
                    Score = p.Score,
                }).ToList(),
            };
        });
}
