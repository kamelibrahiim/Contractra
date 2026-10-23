using Contractra.Application.Identity.Credentials;
using Contractra.Application.Tests.Identity.Fakes;
using Contractra.Domain.Identity;

namespace Contractra.Application.Tests.Identity;

/// <summary>ADR 0007 §6-§7, contract v1.6 §23.4, §23.6: the delivery attempt, the sweeper and the webhook status handling.</summary>
public class DeliverCredentialUseCaseTests
{
    private const string Number = "+201000000001";
    private const string Password = "Pa55word1234";

    private readonly FakeUserRepository _users = new();
    private readonly FakeCredentialIssuanceStore _issuances = new();
    private readonly FakeCredentialSealer _sealer = new();
    private readonly StubWhatsAppSender _sender = new();
    private readonly CredentialDeliveryOptions _options = new() { Enabled = true, MaxAttempts = 3 };
    private readonly FakeClock _clock = new();
    private readonly User _user = new(new LoginIdentifier("eng.ahmed"), UserStatus.Active, "hash", mustChangePassword: false);

    public DeliverCredentialUseCaseTests()
    {
        _users.Seed(_user);
    }

    private DeliverCredentialUseCase CreateUseCase() =>
        new(_issuances, _users, _sealer, _sender, _options, _clock, random: () => 0.5);

    private CredentialIssuance SeedQueued()
    {
        var id = Guid.CreateVersion7();
        var sealedSecret = _sealer.Seal(new SensitivePassword(Password), new SealContext(id, _user.Id));
        var issuance = CredentialIssuance.Queue(
            id, _user.Id, CredentialIssuanceKind.Initial, Guid.CreateVersion7(), _clock.UtcNow,
            Number, "verbal", sealedSecret.Bytes, sealedSecret.Nonce, sealedSecret.KeyId, _options.SealedCopyRetention);
        _issuances.Add(issuance);
        return issuance;
    }

    private Task<DeliveryAttemptOutcome> AttemptAsync(CredentialIssuance issuance, string worker = "w1") =>
        CreateUseCase().ExecuteAsync(issuance.Id, worker);

    // ---- success -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Accepted_SendsTheSealedPasswordToTheStoredNumber_AndKeepsTheCopyUntilAFinalStatus()
    {
        var issuance = SeedQueued();

        var outcome = await AttemptAsync(issuance);

        Assert.Equal(DeliveryAttemptOutcome.Accepted, outcome);
        var sent = Assert.Single(_sender.Sent);
        Assert.Equal((Number, "eng.ahmed", Password), sent);
        Assert.Equal(CredentialDeliveryStatus.Accepted, issuance.Status);
        Assert.Equal("wamid.1", issuance.ProviderMessageId);
        Assert.Equal(1, issuance.Attempts);
        Assert.True(issuance.HasSealedCopy);
    }

    [Fact]
    public async Task TheWorkerNeverChangesTheUsersPassword()
    {
        var issuance = SeedQueued();

        await AttemptAsync(issuance);

        Assert.Equal("hash", _user.PasswordHash);
        Assert.Equal(1, _user.SecurityVersion);
    }

    // ---- retries send the SAME password -------------------------------------------------------------------

    [Fact]
    public async Task ATransientFailure_RetriesLaterWithTheSamePassword()
    {
        var issuance = SeedQueued();
        _sender.Script(new WhatsAppSendResult(WhatsAppSendOutcome.Transient, Category: CredentialFailureCategory.ProviderOutage));

        var first = await AttemptAsync(issuance);
        Assert.Equal(DeliveryAttemptOutcome.WillRetry, first);
        Assert.Equal(CredentialDeliveryStatus.Queued, issuance.Status);
        Assert.True(issuance.NextAttemptAt > _clock.UtcNow);

        // Not due yet.
        Assert.Equal(DeliveryAttemptOutcome.NotClaimed, await AttemptAsync(issuance));

        _clock.UtcNow = issuance.NextAttemptAt!.Value;
        var second = await AttemptAsync(issuance);

        Assert.Equal(DeliveryAttemptOutcome.Accepted, second);
        Assert.Equal(2, _sender.Sent.Count);
        Assert.Equal(_sender.Sent[0].Password, _sender.Sent[1].Password);
        Assert.Equal(2, issuance.Attempts);
    }

    [Fact]
    public async Task TheBackoffGrowsExponentiallyFromTheBaseWithJitter()
    {
        var issuance = SeedQueued();
        _sender.Script(
            new WhatsAppSendResult(WhatsAppSendOutcome.Transient, Category: CredentialFailureCategory.ProviderOutage),
            new WhatsAppSendResult(WhatsAppSendOutcome.Transient, Category: CredentialFailureCategory.ProviderOutage));

        await AttemptAsync(issuance);
        var firstDelay = issuance.NextAttemptAt!.Value - _clock.UtcNow;
        _clock.UtcNow = issuance.NextAttemptAt!.Value;
        await AttemptAsync(issuance);
        var secondDelay = issuance.NextAttemptAt!.Value - _clock.UtcNow;

        // random = 0.5 -> a factor of 0.75 of base * 2^(attempt-1)
        Assert.Equal(TimeSpan.FromMinutes(1) * 0.75, firstDelay);
        Assert.Equal(TimeSpan.FromMinutes(2) * 0.75, secondDelay);
    }

    [Fact]
    public async Task RetryAfterFromTheProvider_IsHonouredWhenLonger()
    {
        var issuance = SeedQueued();
        _sender.Script(new WhatsAppSendResult(
            WhatsAppSendOutcome.Transient, Category: CredentialFailureCategory.RateLimited, RetryAfter: TimeSpan.FromMinutes(10)));

        await AttemptAsync(issuance);

        Assert.Equal(_clock.UtcNow + TimeSpan.FromMinutes(10), issuance.NextAttemptAt);
    }

    [Fact]
    public async Task AfterTheAttemptLimit_TheIssuanceFailsWithTheLastCategory_AndThePasswordIsPurged()
    {
        var issuance = SeedQueued();
        for (var i = 0; i < _options.MaxAttempts; i++)
        {
            _sender.Script(new WhatsAppSendResult(WhatsAppSendOutcome.Transient, Category: CredentialFailureCategory.RateLimited));
        }

        DeliveryAttemptOutcome outcome = default;
        for (var i = 0; i < _options.MaxAttempts; i++)
        {
            _clock.UtcNow = issuance.NextAttemptAt ?? _clock.UtcNow;
            outcome = await AttemptAsync(issuance);
        }

        Assert.Equal(DeliveryAttemptOutcome.Failed, outcome);
        Assert.Equal(CredentialDeliveryStatus.Failed, issuance.Status);
        Assert.Equal(CredentialFailureCategory.RateLimited, issuance.FailureCategory);
        Assert.False(issuance.HasSealedCopy);
    }

    [Fact]
    public async Task ADefinitiveRejection_IsNotRetried_AndPurges()
    {
        var issuance = SeedQueued();
        _sender.Script(new WhatsAppSendResult(WhatsAppSendOutcome.Definitive, Category: CredentialFailureCategory.InvalidNumber));

        var outcome = await AttemptAsync(issuance);

        Assert.Equal(DeliveryAttemptOutcome.Failed, outcome);
        Assert.Equal(CredentialFailureCategory.InvalidNumber, issuance.FailureCategory);
        Assert.False(issuance.HasSealedCopy);
        _clock.UtcNow = _clock.UtcNow.AddHours(1);
        Assert.Equal(DeliveryAttemptOutcome.NotClaimed, await AttemptAsync(issuance));
    }

    [Fact]
    public async Task AnAmbiguousSendException_IsTreatedAsTransient_AndNeverLogsOrKeepsTheException()
    {
        var issuance = SeedQueued();
        _sender.ThrowOnNextSend = new HttpRequestException("boom");

        var outcome = await AttemptAsync(issuance);

        Assert.Equal(DeliveryAttemptOutcome.WillRetry, outcome);
        Assert.Equal(CredentialDeliveryStatus.Queued, issuance.Status);
    }

    // ---- re-checks before opening the sealed copy ---------------------------------------------------------

    [Fact]
    public async Task APastDeadline_FailsAsExpired_WithoutOpeningOrSending()
    {
        var issuance = SeedQueued();
        _clock.UtcNow = issuance.PurgeBy;

        var outcome = await AttemptAsync(issuance);

        Assert.Equal(DeliveryAttemptOutcome.Failed, outcome);
        Assert.Equal(CredentialFailureCategory.ExpiredBeforeDelivery, issuance.FailureCategory);
        Assert.Empty(_sender.Sent);
        Assert.False(issuance.HasSealedCopy);
    }

    [Fact]
    public async Task ADisabledUser_CancelsTheIssuance_WithoutSending()
    {
        var issuance = SeedQueued();
        _user.Disable();

        var outcome = await AttemptAsync(issuance);

        Assert.Equal(DeliveryAttemptOutcome.Cancelled, outcome);
        Assert.Equal(CredentialDeliveryStatus.Cancelled, issuance.Status);
        Assert.Empty(_sender.Sent);
        Assert.False(issuance.HasSealedCopy);
    }

    [Fact]
    public async Task AUserWhoAlreadySignedIn_NeedsNoMessage_AndTheCopyIsPurged()
    {
        var issuance = SeedQueued();
        _issuances.SignIns[_user.Id] = _clock.UtcNow.AddMinutes(1);

        var outcome = await AttemptAsync(issuance);

        Assert.Equal(DeliveryAttemptOutcome.AlreadyUsed, outcome);
        Assert.Empty(_sender.Sent);
        Assert.False(issuance.HasSealedCopy);
    }

    [Fact]
    public async Task ASupersededIssuance_IsNeverSent()
    {
        var issuance = SeedQueued();
        issuance.Supersede(_clock.UtcNow);

        Assert.Equal(DeliveryAttemptOutcome.NotClaimed, await AttemptAsync(issuance));
        Assert.Empty(_sender.Sent);
    }

    // ---- key and tamper failures --------------------------------------------------------------------------

    [Fact]
    public async Task AMissingKey_FailsWithKeyUnavailable_WithoutRetryOrSend_AndPurges()
    {
        var issuance = SeedQueued();
        _sealer.KeyMissingOnOpen = true;

        var outcome = await AttemptAsync(issuance);

        Assert.Equal(DeliveryAttemptOutcome.Failed, outcome);
        Assert.Equal(CredentialFailureCategory.KeyUnavailable, issuance.FailureCategory);
        Assert.Empty(_sender.Sent);
        Assert.False(issuance.HasSealedCopy);
    }

    [Fact]
    public async Task ACopyThatFailsAuthentication_FailsWithoutSending_AndPurges()
    {
        var issuance = SeedQueued();
        // Re-bind the copy to another row, as an attacker moving ciphertext between rows would.
        var other = CredentialIssuance.Queue(
            Guid.CreateVersion7(), _user.Id, CredentialIssuanceKind.Initial, Guid.CreateVersion7(), _clock.UtcNow,
            Number, "verbal", issuance.SealedBytes!, Guid.CreateVersion7().ToByteArray(), "fake-key", _options.SealedCopyRetention);
        _issuances.Add(other);

        var outcome = await AttemptAsync(other);

        Assert.Equal(DeliveryAttemptOutcome.Failed, outcome);
        Assert.Empty(_sender.Sent);
        Assert.False(other.HasSealedCopy);
    }

    // ---- leases: two workers --------------------------------------------------------------------------------

    [Fact]
    public async Task OnlyOneWorkerClaimsAnIssuance()
    {
        var issuance = SeedQueued();

        var first = await AttemptAsync(issuance, "w1");
        var second = await AttemptAsync(issuance, "w2");

        Assert.Equal(DeliveryAttemptOutcome.Accepted, first);
        Assert.Equal(DeliveryAttemptOutcome.NotClaimed, second);
        Assert.Single(_sender.Sent);
    }

    // ---- sweeper ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheSweeper_ExpiresIssuancesPastTheirDeadline_AndPurges()
    {
        var issuance = SeedQueued();
        _clock.UtcNow = issuance.PurgeBy.AddMinutes(1);

        var ended = await new SweepCredentialsUseCase(_issuances, _clock).ExecuteAsync();

        Assert.Equal(1, ended);
        Assert.Equal(CredentialFailureCategory.ExpiredBeforeDelivery, issuance.FailureCategory);
        Assert.False(issuance.HasSealedCopy);
    }

    [Fact]
    public async Task TheSweeper_EndsIssuancesWhoseUserHasSignedIn()
    {
        var issuance = SeedQueued();
        _issuances.SignIns[_user.Id] = _clock.UtcNow.AddMinutes(5);

        var ended = await new SweepCredentialsUseCase(_issuances, _clock).ExecuteAsync();

        Assert.Equal(1, ended);
        Assert.Equal(CredentialDeliveryStatus.Delivered, issuance.Status);
        Assert.False(issuance.HasSealedCopy);
    }

    [Fact]
    public async Task TheSweeper_LeavesCurrentIssuancesAlone()
    {
        var issuance = SeedQueued();

        var ended = await new SweepCredentialsUseCase(_issuances, _clock).ExecuteAsync();

        Assert.Equal(0, ended);
        Assert.True(issuance.IsOpen);
        Assert.True(issuance.HasSealedCopy);
    }

    // ---- webhook status ----------------------------------------------------------------------------------------

    private ApplyWhatsAppStatusUseCase Apply() => new(_issuances, _clock);

    private async Task<CredentialIssuance> AcceptedAsync()
    {
        var issuance = SeedQueued();
        await AttemptAsync(issuance);
        return issuance;
    }

    [Fact]
    public async Task ADeliveredEvent_EndsTheIssuanceAndPurgesTheCopy()
    {
        var issuance = await AcceptedAsync();

        await Apply().ExecuteAsync(issuance.ProviderMessageId!, "delivered", _clock.UtcNow, null, null);

        Assert.Equal(CredentialDeliveryStatus.Delivered, issuance.Status);
        Assert.False(issuance.HasSealedCopy);
    }

    [Fact]
    public async Task ASentEvent_KeepsTheCopy_BecauseAnAsynchronousFailureMayFollow()
    {
        var issuance = await AcceptedAsync();

        await Apply().ExecuteAsync(issuance.ProviderMessageId!, "sent", _clock.UtcNow, null, null);

        Assert.Equal(CredentialDeliveryStatus.Sent, issuance.Status);
        Assert.True(issuance.HasSealedCopy);
    }

    [Fact]
    public async Task AReplayedEvent_ChangesNothingTheSecondTime()
    {
        var issuance = await AcceptedAsync();
        var at = _clock.UtcNow;
        await Apply().ExecuteAsync(issuance.ProviderMessageId!, "sent", at, null, null);
        var savesAfterFirst = _issuances.SaveCount;

        await Apply().ExecuteAsync(issuance.ProviderMessageId!, "sent", at, null, null);

        Assert.Equal(savesAfterFirst, _issuances.SaveCount);
    }

    [Fact]
    public async Task AnOlderStatusArrivingLate_NeverMovesTheIssuanceBackwards()
    {
        var issuance = await AcceptedAsync();
        await Apply().ExecuteAsync(issuance.ProviderMessageId!, "read", _clock.UtcNow, null, null);

        await Apply().ExecuteAsync(issuance.ProviderMessageId!, "sent", _clock.UtcNow.AddSeconds(1), null, null);
        await Apply().ExecuteAsync(issuance.ProviderMessageId!, "failed", _clock.UtcNow.AddSeconds(2), "131026", "x");

        Assert.Equal(CredentialDeliveryStatus.Read, issuance.Status);
    }

    [Fact]
    public async Task AFailedEvent_FailsTheIssuanceWithAMappedCategory()
    {
        var issuance = await AcceptedAsync();

        await Apply().ExecuteAsync(issuance.ProviderMessageId!, "failed", _clock.UtcNow, "131026", "Message undeliverable");

        Assert.Equal(CredentialDeliveryStatus.Failed, issuance.Status);
        Assert.Equal(CredentialFailureCategory.NotOnWhatsApp, issuance.FailureCategory);
        Assert.False(issuance.HasSealedCopy);
    }

    [Fact]
    public async Task AnUnknownMessageId_AndAnUnknownStatus_AreAcknowledgedAndIgnored()
    {
        var issuance = await AcceptedAsync();

        await Apply().ExecuteAsync("wamid.unknown", "delivered", _clock.UtcNow, null, null);
        await Apply().ExecuteAsync(issuance.ProviderMessageId!, "deleted", _clock.UtcNow, null, null);

        Assert.Equal(CredentialDeliveryStatus.Accepted, issuance.Status);
    }

    // ---- number rules -------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("+201000000001", "+20••••••••01")]
    [InlineData("+971501234567", "+97••••••••67")]
    public void TheNumberIsMaskedToItsCountryPrefixAndLastTwoDigits(string number, string masked)
    {
        Assert.Equal(masked, WhatsAppNumber.Mask(number));
    }
}
