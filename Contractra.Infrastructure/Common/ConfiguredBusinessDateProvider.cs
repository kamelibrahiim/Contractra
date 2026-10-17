using Contractra.Application.Common;
using Microsoft.Extensions.Configuration;

namespace Contractra.Infrastructure.Common;

public sealed class ConfiguredBusinessDateProvider : IBusinessDateProvider
{
    private readonly TimeZoneInfo _timeZone;
    private readonly IClock _clock;

    public ConfiguredBusinessDateProvider(IConfiguration configuration, IClock clock)
    {
        var timeZoneId = configuration["Business:TimeZone"] ?? "Africa/Cairo";
        _timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        _clock = clock;
    }

    public DateOnly Today() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_clock.UtcNow, _timeZone).Date);
}
