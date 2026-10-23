using Contractra.Domain.Identity;

namespace Contractra.Application.Identity.Users;

/// <summary>
/// Auth contract v1.5 §9.2 <c>credentialState</c> (engineering decision E3): derived on
/// read, never stored; it tells the Admin which credential action applies.
/// </summary>
public enum CredentialState
{
    /// <summary>The user has chosen their own password.</summary>
    PasswordSet,

    /// <summary>A temporary password is issued, not used yet and not expired.</summary>
    TemporaryPasswordIssued,

    /// <summary>The temporary password expired, or the account predates credentials.</summary>
    InitialPasswordRequired,

    /// <summary>A reset was issued and its code has not expired.</summary>
    ResetPending,

    /// <summary>A reset was issued but its code expired unused.</summary>
    ResetRequired,

    /// <summary>v1.6 (contract §23.1a): the latest credential issuance is still open (queued, sending or awaiting a provider status).</summary>
    DeliveryPending,

    /// <summary>v1.6: the latest credential issuance ended without delivery; re-issue.</summary>
    DeliveryFailed,
}

/// <summary>
/// The facts <see cref="CredentialStateRule"/> needs, plus the listed attributes, for one
/// user at one instant. <paramref name="ActiveRoleCodes"/> are the roles active at that
/// instant, sorted by code. No hash, token or secret is part of it.
/// </summary>
public sealed record UserDirectoryEntry(
    Guid Id,
    string LoginIdentifier,
    UserStatus Status,
    bool IsBootstrapAdmin,
    IReadOnlyList<string> ActiveRoleCodes,
    bool MustChangePassword,
    DateTimeOffset? TemporaryPasswordExpiresAt,
    bool HasUsablePassword,
    DateTimeOffset? OpenResetTicketExpiresAt,
    CredentialDeliveryStatus? LatestIssuanceStatus = null);

/// <summary>Contract §9.2 query: 1-based <paramref name="Page"/>, case-insensitive substring <paramref name="Search"/> of the login identifier, optional status filter.</summary>
public sealed record UserListCriteria(int Page, int PageSize, string? Search, UserStatus? Status)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;
    public const int MaxSearchLength = 256;
}

public sealed record UserDirectoryPage(IReadOnlyList<UserDirectoryEntry> Items, int TotalCount);

/// <summary>
/// Read-only access to users for the Admin endpoints (contract §9.2, §9.3). Sorted by
/// login identifier, case-insensitive ascending. Implemented in Infrastructure.
/// </summary>
public interface IUserDirectory
{
    Task<UserDirectoryPage> ListAsync(UserListCriteria criteria, DateTimeOffset asOf, CancellationToken cancellationToken = default);

    /// <returns>Null when no user has that id.</returns>
    Task<UserDirectoryEntry?> FindAsync(Guid userId, DateTimeOffset asOf, CancellationToken cancellationToken = default);
}

/// <summary>The user object of contract §9.1-§9.5 responses.</summary>
public sealed record UserSummary(
    Guid Id,
    string LoginIdentifier,
    UserStatus Status,
    IReadOnlyList<string> Roles,
    bool IsBootstrapAdmin,
    CredentialState CredentialState);
