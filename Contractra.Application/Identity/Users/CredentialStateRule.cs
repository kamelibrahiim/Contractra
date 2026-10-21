namespace Contractra.Application.Identity.Users;

/// <summary>
/// Auth contract v1.5 §9.2 credentialState table. An account whose password was
/// invalidated by a reset (no usable password) is ResetPending while its open ticket is
/// valid and ResetRequired otherwise; an account that still has its first temporary
/// password is TemporaryPasswordIssued until that password expires, then
/// InitialPasswordRequired (also the state of an account with no recorded expiry, i.e.
/// one created before credentials existed); every other account has chosen its password.
/// A reset code and a temporary password are valid strictly before their expiry instant,
/// like <c>PasswordResetTicket.IsValidAt</c> and the login check.
/// </summary>
public static class CredentialStateRule
{
    public static CredentialState Derive(UserDirectoryEntry entry, DateTimeOffset asOf)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (!entry.HasUsablePassword)
        {
            return entry.OpenResetTicketExpiresAt is { } ticketExpiresAt && ticketExpiresAt > asOf
                ? CredentialState.ResetPending
                : CredentialState.ResetRequired;
        }

        if (entry.MustChangePassword)
        {
            return entry.TemporaryPasswordExpiresAt is { } expiresAt && expiresAt > asOf
                ? CredentialState.TemporaryPasswordIssued
                : CredentialState.InitialPasswordRequired;
        }

        return CredentialState.PasswordSet;
    }

    public static UserSummary ToSummary(UserDirectoryEntry entry, DateTimeOffset asOf) =>
        new(entry.Id, entry.LoginIdentifier, entry.Status, entry.ActiveRoleCodes, entry.IsBootstrapAdmin, Derive(entry, asOf));
}
