using Contractra.Application.Identity.Credentials;
using Contractra.Application.Identity.UseCases;
using Contractra.Application.Tests.Identity.Auth;
using Contractra.Application.Tests.Identity.Fakes;
using Contractra.Domain.Audit;
using Contractra.Domain.Identity;

namespace Contractra.Application.Tests.Identity;

/// <summary>Auth contract v1.6 §23.3 (R50, R51; roadmap OD-1(b), OD-18, OD-29(a) provisional; ADR 0007 §8).</summary>
public class ReissueCredentialUseCaseTests
{
    private const string Number = "+201000000001";

    private static readonly Guid ActorId = Guid.CreateVersion7();

    private readonly FakeUserRepository _users = new();
    private readonly FakePasswordHasher _hasher = new();
    private readonly FakeTemporaryPasswordGenerator _passwords = new();
    private readonly FakeCredentialSealer _sealer = new();
    private readonly StubWhatsAppSender _sender = new();
    private readonly FakeCredentialIssuanceStore _issuances = new();
    private readonly CredentialDeliveryOptions _delivery = new() { Enabled = true };
    private readonly FakeSessionStore _sessions = new();
    private readonly FakeAuditWriter _auditWriter = new();
    private readonly FakeClock _clock = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FakeUserAccessReader _accessReader = new();
    private readonly User _user = new(new LoginIdentifier("eng.ahmed"), UserStatus.Active, "old-hash", mustChangePassword: false);

    public ReissueCredentialUseCaseTests()
    {
        _users.Seed(_user);
    }

    private ReissueCredentialUseCase CreateUseCase() => new(
        _users, _hasher, _passwords, _sealer, _sender, _issuances, _delivery, _sessions, _auditWriter, _clock, _unitOfWork, _accessReader);

    private ReissueCredentialRequest Request(Guid? expected = null, Guid? target = null) =>
        new(target ?? _user.Id, expected, ActorId, "corr-reset", AuditSource.Web);

    /// <summary>An earlier issuance that is old enough not to trip the minimum interval.</summary>
    private CredentialIssuance SeedIssuance(Action<CredentialIssuance>? configure = null, TimeSpan? age = null)
    {
        var issuance = CredentialIssuance.Queue(
            Guid.CreateVersion7(), _user.Id, CredentialIssuanceKind.Initial, ActorId, _clock.UtcNow - (age ?? TimeSpan.FromMinutes(30)),
            Number, "verbal", [1, 2, 3], [4, 5, 6], "k1", TimeSpan.FromHours(24));
        configure?.Invoke(issuance);
        _issuances.Add(issuance);
        return issuance;
    }

    private static void Deliver(CredentialIssuance issuance) => issuance.MarkUsed(issuance.IssuedAt.AddMinutes(1));

    // ---- check order (contract §9 steps 5-7) --------------------------------------------------------------

    [Fact]
    public async Task UnknownUser_Is404()
    {
        var result = await CreateUseCase().ExecuteAsync(Request(target: Guid.CreateVersion7()));

        Assert.Equal("users.not_found", result.Error!.Code);
    }

    [Fact]
    public async Task TheBootstrapAdmin_IsProtected_BeforeAnyStateCheck_AndNothingChanges()
    {
        var admin = User.CreateBootstrapAdmin(
            new LoginIdentifier("boot.admin"), "boot-hash", new Role(new RoleCode("Admin"), RoleStatus.Active),
            new Permission(new PermissionCode(PermissionCode.PayrollView), PermissionStatus.Active), _clock.UtcNow, _clock.UtcNow);
        _users.Seed(admin);
        _delivery.Enabled = false; // a later check would also fail; the bootstrap check must win

        var result = await CreateUseCase().ExecuteAsync(Request(target: admin.Id));

        Assert.Equal("users.bootstrap_account_protected", result.Error!.Code);
        Assert.Equal("boot-hash", admin.PasswordHash);
        Assert.Empty(_auditWriter.Staged);
        Assert.False(_unitOfWork.TransactionOpen);
    }

    [Fact]
    public async Task ADisabledUser_Is409AlreadyDisabled()
    {
        _user.Disable();

        var result = await CreateUseCase().ExecuteAsync(Request());

        Assert.Equal("users.already_disabled", result.Error!.Code);
    }

    [Fact]
    public async Task AnAdminCannotResetTheirOwnAccount()
    {
        var result = await CreateUseCase().ExecuteAsync(new ReissueCredentialRequest(_user.Id, null, _user.Id, "c", AuditSource.Web));

        Assert.Equal("users.self_reset_not_allowed", result.Error!.Code);
        Assert.Equal("old-hash", _user.PasswordHash);
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("sender")]
    [InlineData("sealer")]
    public async Task WhenDeliveryOrTheSealingKeyIsUnavailable_Answers503_AndNothingChanges(string reason)
    {
        var earlier = SeedIssuance(Deliver);
        _ = earlier;
        switch (reason)
        {
            case "disabled": _delivery.Enabled = false; break;
            case "sender": _sender.Configured = false; break;
            default: _sealer.Available = false; break;
        }

        var result = await CreateUseCase().ExecuteAsync(Request());

        Assert.Equal("users.credential_delivery_unavailable", result.Error!.Code);
        Assert.Equal("old-hash", _user.PasswordHash);
        Assert.Equal(1, _user.SecurityVersion);
        Assert.Empty(_sessions.RevokeAllCalls);
        Assert.Single(_issuances.All);
        Assert.Empty(_auditWriter.Staged);
        Assert.False(_unitOfWork.TransactionOpen);
    }

    [Fact]
    public async Task AnAccountWithoutAStoredNumberAndConsent_Is409DestinationMissing()
    {
        var result = await CreateUseCase().ExecuteAsync(Request());

        Assert.Equal("users.whatsapp_destination_missing", result.Error!.Code);
        Assert.Equal("old-hash", _user.PasswordHash);
    }

    // ---- double click / stale screen (expectedIssuanceId) ------------------------------------------------

    [Fact]
    public async Task WhenTheLatestIssuanceIsOpen_TheCallerMustNameIt()
    {
        var open = SeedIssuance();

        var missing = await CreateUseCase().ExecuteAsync(Request());
        var stale = await CreateUseCase().ExecuteAsync(Request(expected: Guid.CreateVersion7()));

        Assert.Equal("users.issuance_changed", missing.Error!.Code);
        Assert.Equal("users.issuance_changed", stale.Error!.Code);
        Assert.Equal(CredentialDeliveryStatus.Queued, open.Status);
        Assert.Equal("old-hash", _user.PasswordHash);
    }

    [Fact]
    public async Task WhenTheLatestIssuanceFailed_TheCallerMustNameIt()
    {
        var failed = SeedIssuance(i => i.Fail(CredentialFailureCategory.NotOnWhatsApp, i.IssuedAt.AddMinutes(1)));

        var missing = await CreateUseCase().ExecuteAsync(Request());
        var named = await CreateUseCase().ExecuteAsync(Request(expected: failed.Id));

        Assert.Equal("users.issuance_changed", missing.Error!.Code);
        Assert.True(named.IsSuccess);
    }

    [Fact]
    public async Task WhenTheLatestIssuanceWasDelivered_NoTokenIsNeeded()
    {
        SeedIssuance(Deliver);

        var result = await CreateUseCase().ExecuteAsync(Request());

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task TwoQuickDoubleClicks_IssueOnce()
    {
        var delivered = SeedIssuance(Deliver);
        var first = await CreateUseCase().ExecuteAsync(Request());

        // The second click still names the issuance the first screen showed.
        var second = await CreateUseCase().ExecuteAsync(Request(expected: delivered.Id));

        Assert.True(first.IsSuccess);
        Assert.False(second.IsSuccess);
        Assert.Equal(2, _issuances.All.Count);
    }

    // ---- limits (OD-18) ----------------------------------------------------------------------------------

    [Fact]
    public async Task ARequestInsideTheMinimumInterval_Is429WithTheWaitInSeconds()
    {
        SeedIssuance(Deliver, age: TimeSpan.FromSeconds(20));

        var result = await CreateUseCase().ExecuteAsync(Request());

        Assert.Equal("users.reissue_too_soon", result.Error!.Code);
        Assert.Equal("40", result.Error.Message);
        Assert.Equal("old-hash", _user.PasswordHash);
    }

    [Fact]
    public async Task AfterTheDailyCapIsReached_Is429()
    {
        for (var i = 0; i < _delivery.MaxReissuesPer24Hours; i++)
        {
            SeedIssuance(Deliver, age: TimeSpan.FromMinutes(2 + i));
        }

        var result = await CreateUseCase().ExecuteAsync(Request());

        Assert.Equal("users.reissue_too_soon", result.Error!.Code);
    }

    // ---- the effects (one transaction) -------------------------------------------------------------------

    [Fact]
    public async Task Success_ReplacesTheHashRevokesEverySessionAndNeverForcesAChange()
    {
        SeedIssuance(Deliver);

        var result = await CreateUseCase().ExecuteAsync(Request());

        Assert.True(result.IsSuccess);
        Assert.True(_unitOfWork.Committed);
        Assert.Equal(_hasher.Hash("Temp1Aa1"), _user.PasswordHash);
        Assert.False(_user.MustChangePassword);
        Assert.Equal(2, _user.SecurityVersion);
        var revoke = Assert.Single(_sessions.RevokeAllCalls);
        Assert.Equal(SessionRevokeReason.PasswordResetIssued, revoke.Reason);
        Assert.Equal(_user.Id, revoke.UserId);
    }

    [Fact]
    public async Task Success_SupersedesTheEarlierOpenIssuanceAndPurgesItsSealedCopy()
    {
        var earlier = SeedIssuance();

        var result = await CreateUseCase().ExecuteAsync(Request(expected: earlier.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal(CredentialDeliveryStatus.Superseded, earlier.Status);
        Assert.False(earlier.HasSealedCopy);
    }

    [Fact]
    public async Task Success_QueuesAResetIssuanceToTheStoredNumberWithItsOwnSealedCopy()
    {
        SeedIssuance(Deliver);

        var result = await CreateUseCase().ExecuteAsync(Request());

        var latest = _issuances.All.OrderByDescending(i => i.IssuedAt).First();
        Assert.Equal(latest.Id, result.Value!.IssuanceId);
        Assert.Equal(CredentialIssuanceKind.Reset, latest.Kind);
        Assert.Equal(CredentialDeliveryStatus.Queued, latest.Status);
        Assert.Equal(Number, latest.Destination);
        Assert.Equal("verbal", latest.ConsentBasis);
        Assert.Equal(ActorId, latest.IssuedByUserId);
        var opened = _sealer.Open(
            new SealedSecret(latest.SealedBytes!, latest.SealNonce!, latest.SealKeyId!), new SealContext(latest.Id, latest.UserId));
        Assert.Equal("Temp1Aa1", opened.Reveal());
        Assert.Empty(_sender.Sent);
    }

    [Fact]
    public async Task Success_AuditsPasswordResetIssued_WithoutAnySecret()
    {
        SeedIssuance(Deliver);

        var result = await CreateUseCase().ExecuteAsync(Request());

        var audit = Assert.Single(_auditWriter.Staged);
        AuditAssert.Event(
            audit, AuditActionType.PasswordResetIssued, ActorId, _user.Id, "corr-reset", AuditSource.Web, _clock.UtcNow,
            actorRoleCodeStillPendingOc6: false, expectedActorRoleCode: null);
        AuditAssert.SnapshotKeys(audit, "credential_issuance_id", "issued_by_user_id", "channel", "issued_at");
        var snapshot = AuditAssert.SnapshotOf(audit);
        Assert.Equal(result.Value!.IssuanceId.ToString(), snapshot["credential_issuance_id"]);
        Assert.Equal("whatsapp", snapshot["channel"]);
        AuditAssert.ContainsNone(audit, "Temp1Aa1", _user.PasswordHash, Number, "1000000001");
    }

    [Fact]
    public async Task AFailedSave_ChangesNothingThatCommitted()
    {
        SeedIssuance(Deliver);
        _unitOfWork.ThrowOnSave = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateUseCase().ExecuteAsync(Request()));

        Assert.False(_unitOfWork.Committed);
        Assert.Empty(_sender.Sent);
    }
}
