using Contractra.Domain.Parties;

namespace Contractra.Domain.Identity;

public sealed class User
{
    /// <summary>
    /// Reserved password_hash value meaning "this account has no usable
    /// password": a password reset is pending, or the account existed before
    /// credentials were stored. It is not valid base64, so no password-hash
    /// format can accept it; the Application layer checks HasUsablePassword and
    /// never passes it to a hasher (auth contract v1.5 §5.1, plan E1/E2).
    /// </summary>
    public const string UnusablePasswordHash = "!";

    /// <summary>
    /// Security version of a new user, and of rows that existed before the
    /// column (auth contract v1.5 §3.5, plan E7).
    /// </summary>
    public const int InitialSecurityVersion = 1;

    private const string AdminRoleCode = "Admin";

    private readonly List<UserRole> _roles = new();
    private readonly List<UserPermission> _permissions = new();

    public Guid Id { get; private set; }

    public LoginIdentifier LoginIdentifier { get; private set; }

    public UserStatus Status { get; private set; }

    public IReadOnlyCollection<UserRole> Roles => _roles.AsReadOnly();

    public IReadOnlyCollection<UserPermission> Permissions => _permissions.AsReadOnly();

    public UserPartyLink? PartyLink { get; private set; }

    /// <summary>
    /// The stored password hash, computed by the Application layer. The Domain
    /// never hashes or verifies a password; it only keeps the string.
    /// </summary>
    public string PasswordHash { get; private set; } = null!;

    /// <summary>
    /// True while the account is on a temporary password it has never changed
    /// (owner decision R1). While true, the user may call only the endpoints in
    /// auth contract §7.
    /// </summary>
    public bool MustChangePassword { get; private set; }

    /// <summary>
    /// When the current temporary password stops authenticating; set exactly
    /// when a temporary password is issued (plan E4).
    /// </summary>
    public DateTimeOffset? TemporaryPasswordExpiresAt { get; private set; }

    /// <summary>
    /// Incremented by exactly one on every successful security-state change, so
    /// access tokens issued before the change are rejected (owner decisions R5,
    /// R34; plan E7). Rejected changes leave it untouched.
    /// </summary>
    public int SecurityVersion { get; private set; }

    /// <summary>
    /// Set only by <see cref="CreateBootstrapAdmin"/>; nothing changes it
    /// afterwards (owner decision R20; the database also forbids changing it).
    /// </summary>
    public bool IsBootstrapAdmin { get; private set; }

    public bool HasUsablePassword => PasswordHash != UnusablePasswordHash;

    private User()
    {
    }

    /// <param name="passwordHash">The hash computed by the Application layer.</param>
    /// <param name="mustChangePassword">True when <paramref name="passwordHash"/> is a temporary password.</param>
    /// <param name="temporaryPasswordExpiresAt">Required, in UTC, exactly when <paramref name="mustChangePassword"/> is true.</param>
    public User(
        LoginIdentifier loginIdentifier,
        UserStatus status,
        string passwordHash,
        bool mustChangePassword,
        DateTimeOffset? temporaryPasswordExpiresAt = null)
        : this(Guid.CreateVersion7(), loginIdentifier, status, passwordHash, mustChangePassword, temporaryPasswordExpiresAt)
    {
    }

    public User(
        Guid id,
        LoginIdentifier loginIdentifier,
        UserStatus status,
        string passwordHash,
        bool mustChangePassword,
        DateTimeOffset? temporaryPasswordExpiresAt = null)
    {
        ValidatePasswordHash(passwordHash, nameof(passwordHash));

        if (mustChangePassword != temporaryPasswordExpiresAt.HasValue)
        {
            throw new ArgumentException(
                "A temporary-password expiry is required exactly when the password must be changed.",
                nameof(temporaryPasswordExpiresAt));
        }

        if (temporaryPasswordExpiresAt is { } expiresAt)
        {
            EnsureUtc(expiresAt, nameof(temporaryPasswordExpiresAt));
        }

        Id = id;
        LoginIdentifier = loginIdentifier;
        Status = status;
        PasswordHash = passwordHash;
        MustChangePassword = mustChangePassword;
        TemporaryPasswordExpiresAt = temporaryPasswordExpiresAt;
        SecurityVersion = InitialSecurityVersion;
    }

    /// <summary>
    /// The only way to create the bootstrap admin (auth contract §11): Active,
    /// never forced to change its password (R19), assigned the Admin role and
    /// granted Payroll.View directly — never through the role (C3, BR-235).
    /// </summary>
    public static User CreateBootstrapAdmin(
        LoginIdentifier loginIdentifier,
        string passwordHash,
        Role adminRole,
        Permission payrollView,
        DateTimeOffset createdAt,
        DateTimeOffset asOf)
    {
        ArgumentNullException.ThrowIfNull(adminRole);
        ArgumentNullException.ThrowIfNull(payrollView);
        ValidateUsablePasswordHash(passwordHash, nameof(passwordHash));

        if (!string.Equals(adminRole.Code.Value, AdminRoleCode, StringComparison.OrdinalIgnoreCase)
            || adminRole.Status != RoleStatus.Active)
        {
            throw new ArgumentException($"The bootstrap admin must be assigned the active '{AdminRoleCode}' role.", nameof(adminRole));
        }

        if (!payrollView.Code.IsPayrollView)
        {
            throw new ArgumentException($"The bootstrap admin is granted '{PermissionCode.PayrollView}' directly.", nameof(payrollView));
        }

        var user = new User(loginIdentifier, UserStatus.Active, passwordHash, mustChangePassword: false);
        user.AssignRoleCore(adminRole, createdAt, asOf);
        user.GrantPermissionCore(payrollView, createdAt, asOf);
        user.IsBootstrapAdmin = true;
        return user;
    }

    public void Disable()
    {
        EnsureNotBootstrapAdmin(nameof(Disable));

        if (Status != UserStatus.Active)
        {
            throw new InvalidOperationException($"Only an Active user can be disabled; current status is {Status}.");
        }

        Status = UserStatus.Disabled;
        BumpSecurityVersion();
    }

    public void Reactivate()
    {
        EnsureNotBootstrapAdmin(nameof(Reactivate));

        if (Status != UserStatus.Disabled)
        {
            throw new InvalidOperationException($"Only a Disabled user can be reactivated; current status is {Status}.");
        }

        Status = UserStatus.Active;
        BumpSecurityVersion();
    }

    /// <summary>
    /// Assigns a role from assignedAt onwards. The caller supplies "now" as asOf;
    /// assignedAt is a historical fact, not a scheduled future start.
    /// A role may be reassigned after an earlier assignment ended, but never so
    /// that two assignments of the same role overlap. Only the assignments loaded
    /// on this instance are checked; the database is the final guard.
    /// </summary>
    public UserRole AssignRole(Role role, DateTimeOffset assignedAt, DateTimeOffset asOf)
    {
        ArgumentNullException.ThrowIfNull(role);
        EnsureNotBootstrapAdmin(nameof(AssignRole));

        return AssignRoleCore(role, assignedAt, asOf);
    }

    /// <summary>
    /// Ends the open assignment of this role (R34: the only public way to end a
    /// role, so the security version always moves with it).
    /// </summary>
    public void EndRole(Role role, DateTimeOffset endedAt, DateTimeOffset asOf)
    {
        ArgumentNullException.ThrowIfNull(role);
        EnsureNotBootstrapAdmin(nameof(EndRole));

        var open = _roles.SingleOrDefault(r => r.RoleId == role.Id && r.EndedAt is null)
            ?? throw new InvalidOperationException($"Role '{role.Code}' has no open assignment on this user.");

        open.End(endedAt, asOf);
        BumpSecurityVersion();
    }

    /// <summary>
    /// Grants a permission directly to this user from grantedAt onwards. Unlike
    /// Role.GrantPermission, Payroll.View is allowed: per BR-235 / FR-AUTH-006 it
    /// is granted explicitly per user, never through a role. The caller supplies
    /// "now" as asOf. A permission may be granted again after an earlier grant was
    /// revoked, but two grants of the same permission never overlap. Only the
    /// grants loaded on this instance are checked; the database is the final guard.
    /// </summary>
    public UserPermission GrantPermission(Permission permission, DateTimeOffset grantedAt, DateTimeOffset asOf)
    {
        ArgumentNullException.ThrowIfNull(permission);
        EnsureNotBootstrapAdmin(nameof(GrantPermission));

        return GrantPermissionCore(permission, grantedAt, asOf);
    }

    /// <summary>
    /// Revokes the active direct grant of this permission (R34: the only public
    /// way to revoke, so the security version always moves with it).
    /// </summary>
    public void RevokePermission(Permission permission, DateTimeOffset revokedAt, DateTimeOffset asOf)
    {
        ArgumentNullException.ThrowIfNull(permission);
        EnsureNotBootstrapAdmin(nameof(RevokePermission));

        var active = _permissions.SingleOrDefault(p => p.PermissionId == permission.Id && p.RevokedAt is null)
            ?? throw new InvalidOperationException($"Permission '{permission.Code}' has no active direct grant on this user.");

        active.Revoke(revokedAt, asOf);
        BumpSecurityVersion();
    }

    /// <summary>
    /// Sets a password the user chose: their own change (forced or voluntary),
    /// the completion of a reset, or the bootstrap break-glass command. Clears
    /// the forced change and any temporary-password expiry.
    /// </summary>
    public void ChangePassword(string newPasswordHash)
    {
        ValidateUsablePasswordHash(newPasswordHash, nameof(newPasswordHash));
        EnsureActive(nameof(ChangePassword));

        PasswordHash = newPasswordHash;
        MustChangePassword = false;
        TemporaryPasswordExpiresAt = null;
        BumpSecurityVersion();
    }

    /// <summary>
    /// First step of a password reset (owner instruction R44, plan E1): the
    /// current password stops working at once; the user sets a new one by
    /// completing the reset. Not for an account still on its first temporary
    /// password — that is re-armed with <see cref="ReissueTemporaryPassword"/>.
    /// </summary>
    public void BeginPasswordReset()
    {
        EnsureNotBootstrapAdmin(nameof(BeginPasswordReset));
        EnsureActive(nameof(BeginPasswordReset));

        if (MustChangePassword)
        {
            throw new InvalidOperationException(
                "This account is still on its initial temporary password; re-issue it instead of resetting.");
        }

        PasswordHash = UnusablePasswordHash;
        BumpSecurityVersion();
    }

    /// <summary>
    /// Replaces the temporary password of an account that has never chosen its
    /// own password (plan E3). The caller supplies "now" as asOf.
    /// </summary>
    public void ReissueTemporaryPassword(string temporaryPasswordHash, DateTimeOffset expiresAt, DateTimeOffset asOf)
    {
        ValidateUsablePasswordHash(temporaryPasswordHash, nameof(temporaryPasswordHash));
        EnsureUtc(expiresAt, nameof(expiresAt));

        if (expiresAt <= asOf)
        {
            throw new ArgumentException("expiresAt must be after asOf.", nameof(expiresAt));
        }

        EnsureNotBootstrapAdmin(nameof(ReissueTemporaryPassword));
        EnsureActive(nameof(ReissueTemporaryPassword));

        if (!MustChangePassword)
        {
            throw new InvalidOperationException(
                "This user has already chosen a password; issue a password reset instead.");
        }

        PasswordHash = temporaryPasswordHash;
        TemporaryPasswordExpiresAt = expiresAt;
        BumpSecurityVersion();
    }

    /// <summary>
    /// Links this account to its commercial party. A user links to at most one
    /// party; only a loaded link is checked here, and the database's
    /// UNIQUE(user_id) and UNIQUE(party_id) are the final guards.
    /// </summary>
    public UserPartyLink LinkToParty(Party party)
    {
        ArgumentNullException.ThrowIfNull(party);

        if (PartyLink is not null)
        {
            throw new InvalidOperationException($"This user is already linked to party {PartyLink.PartyId}.");
        }

        PartyLink = new UserPartyLink(Id, party.Id);
        return PartyLink;
    }

    private UserRole AssignRoleCore(Role role, DateTimeOffset assignedAt, DateTimeOffset asOf)
    {
        if (assignedAt > asOf)
        {
            throw new ArgumentException("assignedAt must not be after asOf.", nameof(assignedAt));
        }

        // The new assignment is open-ended, so it overlaps any earlier one of the
        // same role that is still open or ends after the new start.
        if (_roles.Any(r => r.RoleId == role.Id && (r.EndedAt is null || r.EndedAt > assignedAt)))
        {
            throw new InvalidOperationException(
                $"Role '{role.Code}' is already assigned to this user for a period that overlaps {assignedAt:O}.");
        }

        var userRole = new UserRole(Id, role.Id, assignedAt);
        _roles.Add(userRole);
        BumpSecurityVersion();
        return userRole;
    }

    private UserPermission GrantPermissionCore(Permission permission, DateTimeOffset grantedAt, DateTimeOffset asOf)
    {
        if (grantedAt > asOf)
        {
            throw new ArgumentException("grantedAt must not be after asOf.", nameof(grantedAt));
        }

        // The new grant is open-ended, so it overlaps any earlier grant of the same
        // permission that is still active or was revoked after the new start.
        if (_permissions.Any(p => p.PermissionId == permission.Id && (p.RevokedAt is null || p.RevokedAt > grantedAt)))
        {
            throw new InvalidOperationException(
                $"Permission '{permission.Code}' is already granted to this user for a period that overlaps {grantedAt:O}.");
        }

        var userPermission = new UserPermission(Id, permission.Id, grantedAt);
        _permissions.Add(userPermission);
        BumpSecurityVersion();
        return userPermission;
    }

    private void BumpSecurityVersion() => SecurityVersion = checked(SecurityVersion + 1);

    private void EnsureNotBootstrapAdmin(string operation)
    {
        if (IsBootstrapAdmin)
        {
            throw new InvalidOperationException(
                $"The bootstrap admin is protected; '{operation}' is not allowed on it (auth contract §11).");
        }
    }

    private void EnsureActive(string operation)
    {
        if (Status != UserStatus.Active)
        {
            throw new InvalidOperationException($"'{operation}' requires an Active user; current status is {Status}.");
        }
    }

    private static void ValidatePasswordHash(string? passwordHash, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new ArgumentException("Password hash cannot be null, empty, or whitespace.", parameterName);
        }

        if (passwordHash.Length != passwordHash.Trim().Length)
        {
            throw new ArgumentException("Password hash cannot have leading or trailing whitespace.", parameterName);
        }
    }

    private static void ValidateUsablePasswordHash(string? passwordHash, string parameterName)
    {
        ValidatePasswordHash(passwordHash, parameterName);

        if (passwordHash == UnusablePasswordHash)
        {
            throw new ArgumentException("The unusable-password marker is not a password hash.", parameterName);
        }
    }

    // Rejected rather than converted, as for UserRole and UserPermission.
    private static void EnsureUtc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamp must be in UTC (offset 00:00).", parameterName);
        }
    }
}
