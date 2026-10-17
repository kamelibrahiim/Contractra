namespace Contractra.Application.Identity.UseCases;

/// <summary>Auth contract v1.5 §9.1 response. <paramref name="TemporaryPassword"/> is returned once, never stored in plaintext.</summary>
public sealed record CreateUserResult(
    Guid UserId,
    string LoginIdentifier,
    IReadOnlyList<string> RoleCodes,
    string TemporaryPassword,
    DateTimeOffset TemporaryPasswordExpiresAt);
