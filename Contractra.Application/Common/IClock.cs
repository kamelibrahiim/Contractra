namespace Contractra.Application.Common;

/// <summary>
/// The current time, read once per use case and shared with the business
/// change and audit event it records (ADR 0006 E26). Never the database's
/// clock.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
