using Contractra.Application.Audit;
using Contractra.Application.Common;
using Contractra.Application.Parties;
using Contractra.Domain.Audit;
using Contractra.Domain.Identity;
using Contractra.Domain.Parties;

namespace Contractra.Application.Identity.UseCases;

/// <summary>
/// Auth contract v1.5 §9.1, owner decisions R2, R9, R30, R31, R43; engineering
/// decisions E4, E8, E13. One role at least; Engineer requires a valid,
/// unlinked Party on the business date; a random temporary password is
/// issued and returned once.
/// </summary>
public sealed class CreateUserUseCase
{
    private const string EngineerRoleCode = "Engineer";
    private const int MaxLoginIdentifierLength = 256;

    private readonly IUserRepository _users;
    private readonly IRoleRepository _roles;
    private readonly IPartyRepository _parties;
    private readonly IPasswordHasher _hasher;
    private readonly ITemporaryPasswordGenerator _temporaryPasswords;
    private readonly IAuditWriter _auditWriter;
    private readonly IClock _clock;
    private readonly IBusinessDateProvider _businessDate;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeSpan _temporaryPasswordLifetime;

    public CreateUserUseCase(
        IUserRepository users,
        IRoleRepository roles,
        IPartyRepository parties,
        IPasswordHasher hasher,
        ITemporaryPasswordGenerator temporaryPasswords,
        IAuditWriter auditWriter,
        IClock clock,
        IBusinessDateProvider businessDate,
        IUnitOfWork unitOfWork,
        TimeSpan? temporaryPasswordLifetime = null)
    {
        _users = users;
        _roles = roles;
        _parties = parties;
        _hasher = hasher;
        _temporaryPasswords = temporaryPasswords;
        _auditWriter = auditWriter;
        _clock = clock;
        _businessDate = businessDate;
        _unitOfWork = unitOfWork;
        _temporaryPasswordLifetime = temporaryPasswordLifetime ?? TimeSpan.FromHours(72);
    }

    public async Task<UseCaseResult<CreateUserResult>> ExecuteAsync(
        CreateUserRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.LoginIdentifier)
            || request.LoginIdentifier != request.LoginIdentifier.Trim()
            || request.LoginIdentifier.Length > MaxLoginIdentifierLength)
        {
            return Fail("users.login_identifier_invalid", "The login identifier is invalid.");
        }

        if (request.RoleCodes.Count == 0)
        {
            return Fail("users.role_required", "At least one role is required.");
        }

        if (request.RoleCodes.Select(c => c.ToLowerInvariant()).Distinct().Count() != request.RoleCodes.Count)
        {
            return Fail("validation.failed", "Duplicate role codes are not allowed.");
        }

        var isEngineer = request.RoleCodes.Any(c => string.Equals(c, EngineerRoleCode, StringComparison.OrdinalIgnoreCase));

        if (isEngineer && request.PartyId is null)
        {
            return Fail("users.party_required", "partyId is required when roleCodes includes Engineer.");
        }

        if (!isEngineer && request.PartyId is not null)
        {
            return Fail("validation.failed", "partyId must be absent unless roleCodes includes Engineer.");
        }

        var roles = new List<Role>();
        foreach (var code in request.RoleCodes)
        {
            var role = await _roles.FindByCodeAsync(code, cancellationToken);
            if (role is null)
            {
                return Fail("users.role_unknown", $"Role '{code}' does not exist.");
            }

            if (role.Status != RoleStatus.Active)
            {
                return Fail("users.role_unknown", $"Role '{code}' is not active.");
            }

            roles.Add(role);
        }

        if (await _users.ExistsWithLoginIdentifierAsync(request.LoginIdentifier, cancellationToken))
        {
            return Fail("users.login_identifier_taken", "This login identifier is already taken.");
        }

        Party? party = null;
        if (isEngineer)
        {
            var partyId = request.PartyId!.Value;
            party = await _parties.FindByIdAsync(partyId, cancellationToken);

            if (party is null)
            {
                return Fail("users.party_invalid", "not_found");
            }

            if (party.Status != PartyStatus.Active)
            {
                return Fail("users.party_invalid", "not_active");
            }

            var businessDate = _businessDate.Today();
            var hasActiveEngineerRole = party.Roles.Any(r =>
                string.Equals(r.RoleCode.Value, EngineerRoleCode, StringComparison.OrdinalIgnoreCase)
                && r.IsActiveOn(businessDate));

            if (!hasActiveEngineerRole)
            {
                return Fail("users.party_invalid", "no_active_engineer_role");
            }

            if (await _parties.IsLinkedToAnyUserAsync(partyId, cancellationToken))
            {
                return Fail("users.party_already_linked", "This party is already linked to a user.");
            }
        }

        var now = _clock.UtcNow;
        var temporaryPassword = _temporaryPasswords.Generate();
        var temporaryPasswordHash = _hasher.Hash(temporaryPassword);
        var expiresAt = now.Add(_temporaryPasswordLifetime);

        var user = new User(
            new LoginIdentifier(request.LoginIdentifier),
            UserStatus.Active,
            temporaryPasswordHash,
            mustChangePassword: true,
            expiresAt);

        foreach (var role in roles)
        {
            user.AssignRole(role, now, now);
        }

        if (party is not null)
        {
            user.LinkToParty(party);
        }

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        _users.Add(user);

        // actor_role_code: contract §14.1 would resolve this to the role
        // through which the actor held Users.Manage, but §19 OC-6 lists that
        // rule as still needing owner confirmation, and no permission-to-role
        // resolution exists anywhere in the codebase yet. Left null on every
        // event below rather than implementing an unconfirmed rule.
        _auditWriter.Stage(AuditEvent.ForUser(
            AuditActionType.UserCreated,
            actorUserId: request.ActorUserId,
            actorRoleCode: null,
            entityType: "User",
            entityId: user.Id,
            occurredAtUtc: now,
            correlationId: request.CorrelationId,
            source: request.Source));

        foreach (var role in roles)
        {
            var assignment = user.Roles.Single(r => r.RoleId == role.Id);
            _auditWriter.Stage(AuditEvent.ForUser(
                AuditActionType.RoleAssigned,
                actorUserId: request.ActorUserId,
                actorRoleCode: null,
                entityType: "User",
                entityId: user.Id,
                occurredAtUtc: now,
                correlationId: request.CorrelationId,
                source: request.Source,
                snapshot: new Dictionary<string, string?>
                {
                    ["user_role_id"] = assignment.Id.ToString(),
                    ["role_id"] = role.Id.ToString(),
                    ["role_code"] = role.Code.Value,
                    ["assigned_at"] = assignment.AssignedAt.ToString("O"),
                    ["ended_at"] = null,
                }));
        }

        await _unitOfWork.SaveChangesAndCommitAsync(cancellationToken);

        return UseCaseResult<CreateUserResult>.Success(new CreateUserResult(
            user.Id,
            user.LoginIdentifier.Value,
            request.RoleCodes,
            temporaryPassword,
            expiresAt));
    }

    private static UseCaseResult<CreateUserResult> Fail(string code, string message) =>
        UseCaseResult<CreateUserResult>.Failure(code, message);
}
