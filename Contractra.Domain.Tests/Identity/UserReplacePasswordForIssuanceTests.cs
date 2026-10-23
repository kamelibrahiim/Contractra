using static Contractra.Domain.Tests.Identity.TestUsers;

namespace Contractra.Domain.Tests.Identity;

public class UserReplacePasswordForIssuanceTests
{
    [Fact]
    public void ReplacesTheHash_NeverForcesAChange_AndBumpsTheVersion()
    {
        var user = Active();

        user.ReplacePasswordForIssuance(OtherHash);

        Assert.Equal(OtherHash, user.PasswordHash);
        Assert.False(user.MustChangePassword);
        Assert.Null(user.TemporaryPasswordExpiresAt);
        Assert.Equal(2, user.SecurityVersion);
    }

    [Fact]
    public void ClearsALegacyForcedChange()
    {
        var user = WithTemporaryPassword();

        user.ReplacePasswordForIssuance(OtherHash);

        Assert.False(user.MustChangePassword);
        Assert.Null(user.TemporaryPasswordExpiresAt);
    }

    [Fact]
    public void Throws_ForADisabledUser_AndChangesNothing()
    {
        var user = Active();
        user.Disable();

        Assert.Throws<InvalidOperationException>(() => user.ReplacePasswordForIssuance(OtherHash));
        Assert.Equal(Hash, user.PasswordHash);
        Assert.Equal(2, user.SecurityVersion);
    }

    [Fact]
    public void Throws_ForTheBootstrapAdmin()
    {
        var user = BootstrapAdmin();

        Assert.Throws<InvalidOperationException>(() => user.ReplacePasswordForIssuance(OtherHash));
        Assert.Equal(Hash, user.PasswordHash);
    }

    [Theory]
    [InlineData("")]
    [InlineData("!")]
    public void RejectsAnUnusableHash(string hash)
    {
        var user = Active();

        Assert.ThrowsAny<ArgumentException>(() => user.ReplacePasswordForIssuance(hash));
        Assert.Equal(Hash, user.PasswordHash);
    }
}
