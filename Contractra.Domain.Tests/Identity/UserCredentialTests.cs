using Contractra.Domain.Identity;
using static Contractra.Domain.Tests.Identity.TestUsers;

namespace Contractra.Domain.Tests.Identity;

/// <summary>
/// Credential state on the User aggregate (Backend Sprint 2, US-BE-007;
/// auth contract v1.5 §3.5, §7, §9.6, §9.8, §10).
/// </summary>
public class UserCredentialTests
{
    [Fact]
    public void Constructor_StoresTheHashAndTheFlag_AndStartsAtSecurityVersionOne()
    {
        var user = new User(new LoginIdentifier("Cred.User01"), UserStatus.Active, Hash, mustChangePassword: false);

        Assert.Equal(Hash, user.PasswordHash);
        Assert.False(user.MustChangePassword);
        Assert.Null(user.TemporaryPasswordExpiresAt);
        Assert.Equal(1, user.SecurityVersion);
        Assert.False(user.IsBootstrapAdmin);
        Assert.True(user.HasUsablePassword);
    }

    [Fact]
    public void Constructor_WithATemporaryPassword_RequiresTheChangeAndKeepsTheExpiry()
    {
        var expiresAt = T0.AddHours(72);

        var user = new User(new LoginIdentifier("Cred.User02"), UserStatus.Active, Hash, mustChangePassword: true, temporaryPasswordExpiresAt: expiresAt);

        Assert.True(user.MustChangePassword);
        Assert.Equal(expiresAt, user.TemporaryPasswordExpiresAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" " + Hash)]
    [InlineData(Hash + " ")]
    public void Constructor_RejectsAMissingOrPaddedHash(string? hash)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            new User(new LoginIdentifier("Cred.User03"), UserStatus.Active, hash!, mustChangePassword: false));
    }

    [Fact]
    public void Constructor_RequiresAnExpiry_WhenThePasswordMustBeChanged()
    {
        Assert.Throws<ArgumentException>(() =>
            new User(new LoginIdentifier("Cred.User04"), UserStatus.Active, Hash, mustChangePassword: true));
    }

    [Fact]
    public void Constructor_RejectsAnExpiry_WhenThePasswordNeedNotBeChanged()
    {
        Assert.Throws<ArgumentException>(() =>
            new User(new LoginIdentifier("Cred.User05"), UserStatus.Active, Hash, mustChangePassword: false, temporaryPasswordExpiresAt: T0));
    }

    [Fact]
    public void Constructor_RejectsANonUtcExpiry()
    {
        var cairo = new DateTimeOffset(2026, 10, 4, 11, 0, 0, TimeSpan.FromHours(3));

        Assert.Throws<ArgumentException>(() =>
            new User(new LoginIdentifier("Cred.User06"), UserStatus.Active, Hash, mustChangePassword: true, temporaryPasswordExpiresAt: cairo));
    }

    [Fact]
    public void UnusableMarker_MeansNoUsablePassword()
    {
        var user = new User(new LoginIdentifier("Cred.User07"), UserStatus.Active, User.UnusablePasswordHash, mustChangePassword: false);

        Assert.False(user.HasUsablePassword);
    }

    [Fact]
    public void ChangePassword_SetsTheHash_ClearsTheForcedChangeAndExpiry_AndBumpsTheVersion()
    {
        var user = WithTemporaryPassword();

        user.ChangePassword(OtherHash);

        Assert.Equal(OtherHash, user.PasswordHash);
        Assert.False(user.MustChangePassword);
        Assert.Null(user.TemporaryPasswordExpiresAt);
        Assert.Equal(2, user.SecurityVersion);
    }

    [Fact]
    public void ChangePassword_RejectsTheUnusableMarker()
    {
        var user = Active();

        Assert.Throws<ArgumentException>(() => user.ChangePassword(User.UnusablePasswordHash));
        Assert.Equal(Hash, user.PasswordHash);
        Assert.Equal(1, user.SecurityVersion);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void ChangePassword_RejectsAnEmptyHash(string hash)
    {
        var user = Active();

        Assert.ThrowsAny<ArgumentException>(() => user.ChangePassword(hash));
        Assert.Equal(1, user.SecurityVersion);
    }

    [Fact]
    public void ChangePassword_Throws_ForADisabledUser_AndChangesNothing()
    {
        var user = WithStatus(UserStatus.Disabled);

        Assert.Throws<InvalidOperationException>(() => user.ChangePassword(OtherHash));
        Assert.Equal(Hash, user.PasswordHash);
        Assert.Equal(1, user.SecurityVersion);
    }

    [Fact]
    public void BeginPasswordReset_InvalidatesThePassword_AndBumpsTheVersion()
    {
        var user = Active();

        user.BeginPasswordReset();

        Assert.Equal(User.UnusablePasswordHash, user.PasswordHash);
        Assert.False(user.HasUsablePassword);
        Assert.False(user.MustChangePassword);
        Assert.Equal(2, user.SecurityVersion);
    }

    [Fact]
    public void BeginPasswordReset_Throws_ForADisabledUser_AndChangesNothing()
    {
        var user = WithStatus(UserStatus.Disabled);

        Assert.Throws<InvalidOperationException>(() => user.BeginPasswordReset());
        Assert.Equal(Hash, user.PasswordHash);
        Assert.Equal(1, user.SecurityVersion);
    }

    [Fact]
    public void BeginPasswordReset_Throws_WhileTheInitialTemporaryPasswordIsPending()
    {
        var user = WithTemporaryPassword();

        Assert.Throws<InvalidOperationException>(() => user.BeginPasswordReset());
        Assert.Equal(Hash, user.PasswordHash);
        Assert.Equal(1, user.SecurityVersion);
    }

    [Fact]
    public void ChangePassword_CompletesAReset()
    {
        var user = Active();
        user.BeginPasswordReset();

        user.ChangePassword(OtherHash);

        Assert.True(user.HasUsablePassword);
        Assert.Equal(OtherHash, user.PasswordHash);
        Assert.Equal(3, user.SecurityVersion);
    }

    [Fact]
    public void ReissueTemporaryPassword_ReplacesTheHashAndExpiry_AndBumpsTheVersion()
    {
        var user = WithTemporaryPassword();
        var newExpiry = T0.AddDays(5);

        user.ReissueTemporaryPassword(OtherHash, newExpiry, T0.AddDays(2));

        Assert.Equal(OtherHash, user.PasswordHash);
        Assert.True(user.MustChangePassword);
        Assert.Equal(newExpiry, user.TemporaryPasswordExpiresAt);
        Assert.Equal(2, user.SecurityVersion);
    }

    [Fact]
    public void ReissueTemporaryPassword_Throws_OnceTheUserHasChosenAPassword()
    {
        var user = Active();

        Assert.Throws<InvalidOperationException>(() => user.ReissueTemporaryPassword(OtherHash, T0.AddDays(3), T0));
        Assert.Equal(Hash, user.PasswordHash);
        Assert.Equal(1, user.SecurityVersion);
    }

    [Fact]
    public void ReissueTemporaryPassword_Throws_ForADisabledUser()
    {
        var user = WithTemporaryPassword();
        user.Disable();

        Assert.Throws<InvalidOperationException>(() => user.ReissueTemporaryPassword(OtherHash, T0.AddDays(3), T0));
        Assert.Equal(Hash, user.PasswordHash);
        Assert.Equal(2, user.SecurityVersion);
    }

    [Fact]
    public void ReissueTemporaryPassword_RejectsAnExpiryThatIsNotAfterAsOf()
    {
        var user = WithTemporaryPassword();

        Assert.Throws<ArgumentException>(() => user.ReissueTemporaryPassword(OtherHash, T0, T0));
        Assert.Equal(1, user.SecurityVersion);
    }

    [Fact]
    public void ReissueTemporaryPassword_RejectsANonUtcExpiry()
    {
        var user = WithTemporaryPassword();
        var cairo = new DateTimeOffset(2026, 10, 5, 11, 0, 0, TimeSpan.FromHours(3));

        Assert.Throws<ArgumentException>(() => user.ReissueTemporaryPassword(OtherHash, cairo, T0));
        Assert.Equal(1, user.SecurityVersion);
    }

    [Fact]
    public void ReissueTemporaryPassword_RejectsTheUnusableMarker()
    {
        var user = WithTemporaryPassword();

        Assert.Throws<ArgumentException>(() => user.ReissueTemporaryPassword(User.UnusablePasswordHash, T0.AddDays(3), T0));
        Assert.Equal(1, user.SecurityVersion);
    }
}
