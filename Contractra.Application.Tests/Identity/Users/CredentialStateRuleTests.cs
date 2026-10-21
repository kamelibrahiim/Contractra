using Contractra.Application.Identity.Users;
using Contractra.Domain.Identity;

namespace Contractra.Application.Tests.Identity.Users;

/// <summary>Auth contract v1.5 §9.2 credentialState table (engineering decision E3).</summary>
public class CredentialStateRuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 8, 0, 0, TimeSpan.Zero);

    private static UserDirectoryEntry Entry(
        bool mustChangePassword = false,
        DateTimeOffset? temporaryPasswordExpiresAt = null,
        bool hasUsablePassword = true,
        DateTimeOffset? openTicketExpiresAt = null) =>
        new(Guid.CreateVersion7(), "eng.ahmed", UserStatus.Active, false, ["Engineer"],
            mustChangePassword, temporaryPasswordExpiresAt, hasUsablePassword, openTicketExpiresAt);

    [Fact]
    public void AUserWhoChoseAPassword_IsPasswordSet() =>
        Assert.Equal(CredentialState.PasswordSet, CredentialStateRule.Derive(Entry(), Now));

    [Fact]
    public void AnUnexpiredTemporaryPassword_IsTemporaryPasswordIssued() =>
        Assert.Equal(
            CredentialState.TemporaryPasswordIssued,
            CredentialStateRule.Derive(Entry(mustChangePassword: true, temporaryPasswordExpiresAt: Now.AddMinutes(1)), Now));

    [Fact]
    public void ATemporaryPasswordExpiringExactlyNow_IsAlreadyExpired() =>
        Assert.Equal(
            CredentialState.InitialPasswordRequired,
            CredentialStateRule.Derive(Entry(mustChangePassword: true, temporaryPasswordExpiresAt: Now), Now));

    [Fact]
    public void AnExpiredTemporaryPassword_IsInitialPasswordRequired() =>
        Assert.Equal(
            CredentialState.InitialPasswordRequired,
            CredentialStateRule.Derive(Entry(mustChangePassword: true, temporaryPasswordExpiresAt: Now.AddHours(-1)), Now));

    [Fact]
    public void AnAccountThatPredatesCredentials_IsInitialPasswordRequired() =>
        Assert.Equal(
            CredentialState.InitialPasswordRequired,
            CredentialStateRule.Derive(Entry(mustChangePassword: true, temporaryPasswordExpiresAt: null), Now));

    [Fact]
    public void AValidOpenResetTicket_IsResetPending() =>
        Assert.Equal(
            CredentialState.ResetPending,
            CredentialStateRule.Derive(Entry(hasUsablePassword: false, openTicketExpiresAt: Now.AddHours(1)), Now));

    [Fact]
    public void AResetTicketExpiringExactlyNow_IsResetRequired() =>
        Assert.Equal(
            CredentialState.ResetRequired,
            CredentialStateRule.Derive(Entry(hasUsablePassword: false, openTicketExpiresAt: Now), Now));

    [Fact]
    public void AnExpiredUnusedResetTicket_IsResetRequired() =>
        Assert.Equal(
            CredentialState.ResetRequired,
            CredentialStateRule.Derive(Entry(hasUsablePassword: false, openTicketExpiresAt: Now.AddHours(-1)), Now));

    [Fact]
    public void NoUsablePasswordAndNoOpenTicket_IsResetRequired() =>
        Assert.Equal(CredentialState.ResetRequired, CredentialStateRule.Derive(Entry(hasUsablePassword: false), Now));

    [Fact]
    public void ToSummary_CarriesTheListedAttributesAndTheDerivedState()
    {
        var entry = Entry(mustChangePassword: true, temporaryPasswordExpiresAt: Now.AddHours(5));

        var summary = CredentialStateRule.ToSummary(entry, Now);

        Assert.Equal(entry.Id, summary.Id);
        Assert.Equal("eng.ahmed", summary.LoginIdentifier);
        Assert.Equal(UserStatus.Active, summary.Status);
        Assert.Equal(["Engineer"], summary.Roles);
        Assert.False(summary.IsBootstrapAdmin);
        Assert.Equal(CredentialState.TemporaryPasswordIssued, summary.CredentialState);
    }
}
