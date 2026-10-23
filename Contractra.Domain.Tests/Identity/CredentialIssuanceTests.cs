using Contractra.Domain.Identity;

namespace Contractra.Domain.Tests.Identity;

public class CredentialIssuanceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 23, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    private static CredentialIssuance NewIssuance(CredentialIssuanceKind kind = CredentialIssuanceKind.Initial) =>
        CredentialIssuance.Queue(
            Guid.CreateVersion7(), Guid.CreateVersion7(), kind, Guid.CreateVersion7(), T0,
            "+201000000001", "verbal", [1, 2, 3, 4], [9, 9, 9], "k1", Retention);

    [Fact]
    public void Queue_StartsQueuedAndOpenWithASealedCopyAndADeadline()
    {
        var issuance = NewIssuance();

        Assert.Equal(CredentialDeliveryStatus.Queued, issuance.Status);
        Assert.True(issuance.IsOpen);
        Assert.True(issuance.HasSealedCopy);
        Assert.Equal(T0.Add(Retention), issuance.PurgeBy);
        Assert.Equal(0, issuance.Attempts);
    }

    [Fact]
    public void Queue_RejectsMissingSealedCopyBadBasisAndNonUtc()
    {
        var id = Guid.CreateVersion7();
        Assert.Throws<ArgumentException>(() => CredentialIssuance.Queue(id, id, CredentialIssuanceKind.Initial, id, T0, "+20100", "verbal", [], [1], "k", Retention));
        Assert.Throws<ArgumentException>(() => CredentialIssuance.Queue(id, id, CredentialIssuanceKind.Initial, id, T0, "+20100", "other", [1], [1], "k", Retention));
        Assert.Throws<ArgumentException>(() => CredentialIssuance.Queue(id, id, CredentialIssuanceKind.Initial, id, T0.ToOffset(TimeSpan.FromHours(2)), "+20100", "verbal", [1], [1], "k", Retention));
        Assert.Throws<ArgumentException>(() => CredentialIssuance.Queue(id, id, CredentialIssuanceKind.Initial, id, T0, "+20100", "verbal", [1], [1], "k", TimeSpan.Zero));
    }

    [Fact]
    public void SealedBytes_ReturnsACopy()
    {
        var issuance = NewIssuance();

        issuance.SealedBytes![0] = 99;

        Assert.Equal(1, issuance.SealedBytes![0]);
    }

    [Fact]
    public void TryClaim_TakesTheLeaseOnceAndCountsTheAttempt()
    {
        var issuance = NewIssuance();

        Assert.True(issuance.TryClaim("w1", T0, TimeSpan.FromMinutes(2)));
        Assert.False(issuance.TryClaim("w2", T0.AddMinutes(1), TimeSpan.FromMinutes(2)));
        Assert.Equal(1, issuance.Attempts);
        Assert.Equal(CredentialDeliveryStatus.Sending, issuance.Status);
        Assert.True(issuance.TryClaim("w2", T0.AddMinutes(3), TimeSpan.FromMinutes(2)));
        Assert.Equal(2, issuance.Attempts);
    }

    [Fact]
    public void TryClaim_RefusesARowThatIsNotYetDue()
    {
        var issuance = NewIssuance();
        issuance.TryClaim("w1", T0, TimeSpan.FromMinutes(1));
        issuance.RecordTransientFailure(CredentialFailureCategory.ProviderOutage, T0, T0.AddMinutes(5), maxAttempts: 5);

        Assert.False(issuance.TryClaim("w1", T0.AddMinutes(1), TimeSpan.FromMinutes(1)));
        Assert.True(issuance.TryClaim("w1", T0.AddMinutes(5), TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void RecordAccepted_KeepsTheSealedCopyBecauseAnAsynchronousFailureMayFollow()
    {
        var issuance = NewIssuance();
        issuance.TryClaim("w", T0, TimeSpan.FromMinutes(1));

        issuance.RecordAccepted("wamid.1", T0.AddSeconds(2));

        Assert.Equal(CredentialDeliveryStatus.Accepted, issuance.Status);
        Assert.True(issuance.HasSealedCopy);
        Assert.True(issuance.IsOpen);
        Assert.Equal("wamid.1", issuance.ProviderMessageId);
    }

    [Fact]
    public void Retries_StopAtTheAttemptLimitAndFailWithTheLastCategory()
    {
        var issuance = NewIssuance();
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            Assert.True(issuance.TryClaim("w", T0.AddMinutes(attempt * 10), TimeSpan.FromMinutes(1)));
            issuance.RecordTransientFailure(CredentialFailureCategory.RateLimited, T0.AddMinutes(attempt * 10), T0.AddMinutes((attempt * 10) + 5), maxAttempts: 3);
        }

        Assert.Equal(CredentialDeliveryStatus.Failed, issuance.Status);
        Assert.Equal(CredentialFailureCategory.RateLimited, issuance.FailureCategory);
        Assert.False(issuance.HasSealedCopy);
    }

    [Fact]
    public void ARetryScheduledPastTheDeadlineFailsInstead()
    {
        var issuance = NewIssuance();
        issuance.TryClaim("w", T0, TimeSpan.FromMinutes(1));

        issuance.RecordTransientFailure(CredentialFailureCategory.ProviderOutage, T0, T0.Add(Retention).AddMinutes(1), maxAttempts: 5);

        Assert.Equal(CredentialDeliveryStatus.Failed, issuance.Status);
        Assert.False(issuance.HasSealedCopy);
    }

    [Theory]
    [InlineData(CredentialDeliveryStatus.Delivered)]
    [InlineData(CredentialDeliveryStatus.Read)]
    public void ProviderDeliveredOrRead_EndsTheIssuanceAndPurgesTheCopy(CredentialDeliveryStatus status)
    {
        var issuance = NewIssuance();
        issuance.TryClaim("w", T0, TimeSpan.FromMinutes(1));
        issuance.RecordAccepted("m", T0);

        Assert.True(issuance.ApplyProviderStatus(status, T0.AddSeconds(5), T0.AddSeconds(6)));

        Assert.Equal(status, issuance.Status);
        Assert.False(issuance.IsOpen);
        Assert.False(issuance.HasSealedCopy);
        Assert.Null(issuance.SealKeyId);
        Assert.NotNull(issuance.PurgedAt);
    }

    [Fact]
    public void ProviderStatuses_NeverMoveBackwardsAndTerminalRowsIgnoreThem()
    {
        var issuance = NewIssuance();
        issuance.TryClaim("w", T0, TimeSpan.FromMinutes(1));
        issuance.RecordAccepted("m", T0);

        Assert.True(issuance.ApplyProviderStatus(CredentialDeliveryStatus.Sent, T0, T0));
        Assert.False(issuance.ApplyProviderStatus(CredentialDeliveryStatus.Accepted, T0, T0));
        Assert.True(issuance.ApplyProviderStatus(CredentialDeliveryStatus.Delivered, T0, T0));
        Assert.False(issuance.ApplyProviderStatus(CredentialDeliveryStatus.Sent, T0, T0));
        Assert.False(issuance.ApplyProviderStatus(CredentialDeliveryStatus.Failed, T0, T0));
        Assert.Equal(CredentialDeliveryStatus.Delivered, issuance.Status);
    }

    [Fact]
    public void AnAsynchronousProviderFailure_FailsTheIssuanceAndPurges()
    {
        var issuance = NewIssuance();
        issuance.TryClaim("w", T0, TimeSpan.FromMinutes(1));
        issuance.RecordAccepted("m", T0);

        Assert.True(issuance.ApplyProviderStatus(CredentialDeliveryStatus.Failed, T0, T0, CredentialFailureCategory.NotOnWhatsApp));

        Assert.Equal(CredentialDeliveryStatus.Failed, issuance.Status);
        Assert.Equal(CredentialFailureCategory.NotOnWhatsApp, issuance.FailureCategory);
        Assert.False(issuance.HasSealedCopy);
    }

    [Fact]
    public void SupersedeAndCancel_EndTheIssuanceAndPurge_AndCannotRepeat()
    {
        var superseded = NewIssuance();
        superseded.Supersede(T0.AddMinutes(1));
        Assert.Equal(CredentialDeliveryStatus.Superseded, superseded.Status);
        Assert.False(superseded.HasSealedCopy);
        Assert.Throws<InvalidOperationException>(() => superseded.Supersede(T0.AddMinutes(2)));

        var cancelled = NewIssuance();
        cancelled.Cancel(T0.AddMinutes(1));
        Assert.Equal(CredentialDeliveryStatus.Cancelled, cancelled.Status);
        Assert.False(cancelled.HasSealedCopy);
        Assert.Throws<InvalidOperationException>(() => cancelled.Fail(CredentialFailureCategory.KeyUnavailable, T0));
    }

    [Fact]
    public void MarkUsed_EndsAnOpenIssuanceAsDeliveredAndIgnoresAnEndedOne()
    {
        var open = NewIssuance();
        open.MarkUsed(T0.AddMinutes(1));
        Assert.Equal(CredentialDeliveryStatus.Delivered, open.Status);
        Assert.False(open.HasSealedCopy);

        var failed = NewIssuance();
        failed.Fail(CredentialFailureCategory.InvalidNumber, T0);
        failed.MarkUsed(T0.AddMinutes(1));
        Assert.Equal(CredentialDeliveryStatus.Failed, failed.Status);
    }

    [Fact]
    public void PurgeSealedCopy_IsIdempotent()
    {
        var issuance = NewIssuance();
        issuance.PurgeSealedCopy(T0.AddHours(25));
        var purgedAt = issuance.PurgedAt;

        issuance.PurgeSealedCopy(T0.AddHours(26));

        Assert.False(issuance.HasSealedCopy);
        Assert.Equal(purgedAt, issuance.PurgedAt);
    }
}
