using Contractra.Application.Common;

namespace Contractra.Application.Tests.Identity.Fakes;

internal sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 16, 8, 0, 0, TimeSpan.Zero);
}
