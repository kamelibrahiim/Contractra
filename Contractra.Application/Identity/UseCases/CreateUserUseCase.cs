using Contractra.Application.Audit;
using Contractra.Application.Common;
using Contractra.Application.Identity.Auth;
using Contractra.Application.Identity.Credentials;
using Contractra.Application.Identity.Usernames;
using Contractra.Application.Parties;
using Contractra.Domain.Audit;
using Contractra.Domain.Identity;
using Contractra.Domain.Parties;

namespace Contractra.Application.Identity.UseCases;

/// <summary>
/// Auth contract v1.6 §23.2, owner decisions R9, R30, R31, R43, R49, R51; engineering decisions E8,
/// E13; ADR 0007. One role at least; an Engineer requires a valid, unlinked Party on the business
/// date. The system generates <b>one</b> password, stores its hash in the user, seals a copy into the
/// credential issuance and queues the WhatsApp delivery - all in one transaction. The password is
/// never returned. Nothing is created when delivery or the sealing key is unavailable.
/// </summary>
public sealed class CreateUserUseCase
{
    private const string EngineerRoleCode = "Engineer";

    private readonly IUserRepository _users;
    private readonly IRoleRepository _roles;
    private readonly IPartyRepository _parties;
    private readonly IPasswordHasher _hasher;
    private readonly ITemporaryPasswordGenerator _passwords;
    private readonly ICredentialSealing _sealing;
    private readonly IWhatsAppSender _sender;
    private readonly ICredentialIssuanceStore _issuances;
    private readonly CredentialDeliveryOptions _delivery;
    private readonly UsernamePolicy _usernamePolicy;
    private readonly IAuditWriter _auditWriter;
    private readonly IClock _clock;
    private readonly IBusinessDateProvider _businessDate;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserAccessReader _accessReader;

    public CreateUserUseCase(
        IUserRepository users,
        IRoleRepository roles,
        IPartyRepository parties,
        IPasswordHasher hasher,
        ITemporaryPasswordGenerator passwords,
        ICredentialSealing sealing,
        IWhatsAppSender sender,
        ICredentialIssuanceStore issuances,
        CredentialDeliveryOptions delivery,
        UsernamePolicy usernamePolicy,
        IAuditWriter auditWriter,
        IClock clock,
        IBusinessDateProvider businessDate,
        IUnitOfWork unitOfWork,
        IUserAccessReader accessReader)
    {
        _users = users;
        _roles = roles;
        _parties = parties;
        _hasher = hasher;
        _passwords = passwords;
        _sealing = sealing;
        _sender = sender;
        _issuances = issuances;
        _delivery = delivery;
        _usernamePolicy = usernamePolicy;
        _auditWriter = auditWriter;
        _clock = clock;
        _businessDate = businessDate;
        _unitOfWork = unitOfWork;
        _accessReader = accessReader;
    }

    public async Task<UseCaseResult<CreateUserResult>> ExecuteAsync(
        CreateUserRequest request, CancellationToken cancellationToken = default)
    {
        if (!_delivery.Enabled || !_sender.IsConfigured || !_sealing.IsAvailable)
        {
            return Fail(UserErrorCodes.CredentialDeliveryUnavailable, "Credential delivery is not available; nothing was created.");
        }

        if (request.LoginIdentifier is null || request.LoginIdentifier.Contains('\0'))
        {
            return Fail(UserErrorCodes.LoginIdentifierInvalid, "bad_characters");
        }

        var validation = _usernamePolicy.Validate(request.LoginIdentifier);
        if (!validation.IsValid)
        {
            return Fail(UserErrorCodes.LoginIdentifierInvalid, UsernameValidation.ReasonCode(validation.Reason!.Value));
        }

        var login = validation.Canonical!;

        if (request.RoleCodes.Count == 0)
        {
            return Fail("users.role_required", "At least one role is required.");
        }

        if (request.RoleCodes.Select(c => c.ToLowerInvariant()).Distinct().Count() != request.RoleCodes.Count)
        {
            return Fail("validation.failed", "Duplicate role codes are not allowed.");
        }

        if (!WhatsAppNumber.TryNormalize(request.WhatsAppNumber, out var destination))
        {
            return Fail(UserErrorCodes.WhatsAppNumberInvalid, "The WhatsApp number is not a valid international number.");
        }

        if (!request.ConsentAffirmed || request.ConsentBasis is not ("written" or "verbal"))
        {
            return Fail(UserErrorCodes.WhatsAppConsentRequired, "The Admin must affirm the employee's consent (basis written or verbal).");
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
            var role = code.Contains('\0') ? null : await _roles.FindByCodeAsync(code, cancellationToken);
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

        // Role codes and the bootstrap identifier are reserved names (username policy v1 §4).
        var activeRoleCodes = await _roles.ListActiveCodesAsync(cancellationToken);
        var bootstrap = await _users.FindBootstrapAdminAsync(cancellationToken);
        if (activeRoleCodes.Any(c => string.Equals(c, login, StringComparison.OrdinalIgnoreCase))
            || (bootstrap is not null && string.Equals(bootstrap.LoginIdentifier.Value, login, StringComparison.OrdinalIgnoreCase)))
        {
            return Fail(UserErrorCodes.LoginIdentifierInvalid, "reserved");
        }

        if (await _users.ExistsWithLoginIdentifierAsync(login, cancellationToken))
        {
            return Fail(UserErrorCodes.LoginIdentifierTaken, "This login identifier is already taken.");
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

        // One password for this operation. Its hash goes into the user; the sealed copy goes into the
        // issuance. The plaintext leaves scope when this method returns.
        var password = new SensitivePassword(_passwords.Generate());
        var user = new User(new LoginIdentifier(login), UserStatus.Active, _hasher.Hash(password.Reveal()), mustChangePassword: false);

        foreach (var role in roles)
        {
            user.AssignRole(role, now, now);
        }

        if (party is not null)
        {
            user.LinkToParty(party);
        }

        var issuanceId = Guid.CreateVersion7();
        var sealedSecret = _sealing.Seal(password, new SealContext(issuanceId, user.Id));
        var issuance = CredentialIssuance.Queue(
            issuanceId, user.Id, CredentialIssuanceKind.Initial, request.ActorUserId, now,
            destination, request.ConsentBasis, sealedSecret.Bytes, sealedSecret.Nonce, sealedSecret.KeyId,
            _delivery.SealedCopyRetention);

        // actor_role_code (contract §14.1, owner decision OC-6): this action requires Users.Manage,
        // so the rule resolves to the active role through which the actor held it.
        var access = await _accessReader.GetAsync(request.ActorUserId, now, cancellationToken);
        var actorRoleCode = ActorRoleCodeRule.Resolve(access, "Users.Manage");

        await _unitOfWork.BeginTransactionAsync(cancellationToken);

        _users.Add(user);
        _issuances.Add(issuance);

        _auditWriter.Stage(AuditEvent.ForUser(
            AuditActionType.UserCreated,
            actorUserId: request.ActorUserId,
            actorRoleCode: actorRoleCode,
            entityType: "User",
            entityId: user.Id,
            occurredAtUtc: now,
            correlationId: request.CorrelationId,
            source: request.Source,
            snapshot: new Dictionary<string, string?>
            {
                ["credential_issuance_id"] = issuance.Id.ToString(),
                ["consent_affirmed"] = "true",
            }));

        foreach (var role in roles)
        {
            var assignment = user.Roles.Single(r => r.RoleId == role.Id);
            _auditWriter.Stage(AuditEvent.ForUser(
                AuditActionType.RoleAssigned,
                actorUserId: request.ActorUserId,
                actorRoleCode: actorRoleCode,
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
            CredentialDeliveryView.From(issuance, now, _delivery.StalledAfter)));
    }

    private static UseCaseResult<CreateUserResult> Fail(string code, string message) =>
        UseCaseResult<CreateUserResult>.Failure(code, message);
}
