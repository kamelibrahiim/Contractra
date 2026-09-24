using Contractra.Domain.Identity;

namespace Contractra.Domain.Tests.Identity;

public class UserTests
{
    private static LoginIdentifier NewLoginIdentifier() => new("Admin.User01");

    [Theory]
    [InlineData(UserStatus.Active)]
    [InlineData(UserStatus.Disabled)]
    public void Constructor_UsesTheExplicitlySuppliedStatus(UserStatus status)
    {
        var user = new User(NewLoginIdentifier(), status);

        Assert.Equal(status, user.Status);
    }

    [Fact]
    public void Constructor_KeepsTheSuppliedLoginIdentifier()
    {
        var loginIdentifier = NewLoginIdentifier();

        var user = new User(loginIdentifier, UserStatus.Active);

        Assert.Equal(loginIdentifier, user.LoginIdentifier);
    }

    [Fact]
    public void Constructor_GeneratesAVersion7Guid_WhenNoIdIsProvided()
    {
        var user = new User(NewLoginIdentifier(), UserStatus.Active);

        Assert.Equal('7', user.Id.ToString()[14]);
    }

    [Fact]
    public void Constructor_KeepsAnExplicitlySuppliedId()
    {
        var id = Guid.CreateVersion7();

        var user = new User(id, NewLoginIdentifier(), UserStatus.Active);

        Assert.Equal(id, user.Id);
    }

    [Fact]
    public void User_HasNoDeleteMethod()
    {
        var methodNames = typeof(User).GetMethods().Select(m => m.Name);

        Assert.DoesNotContain(methodNames, name => name.Contains("Delete", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Disable_ChangesActiveToDisabled()
    {
        var user = new User(NewLoginIdentifier(), UserStatus.Active);

        user.Disable();

        Assert.Equal(UserStatus.Disabled, user.Status);
    }

    [Fact]
    public void Disable_IsIdempotent_WhenAlreadyDisabled()
    {
        var user = new User(NewLoginIdentifier(), UserStatus.Disabled);

        user.Disable();

        Assert.Equal(UserStatus.Disabled, user.Status);
    }

    [Fact]
    public void Reactivate_ChangesDisabledToActive()
    {
        var user = new User(NewLoginIdentifier(), UserStatus.Disabled);

        user.Reactivate();

        Assert.Equal(UserStatus.Active, user.Status);
    }

    [Fact]
    public void Reactivate_IsIdempotent_WhenAlreadyActive()
    {
        var user = new User(NewLoginIdentifier(), UserStatus.Active);

        user.Reactivate();

        Assert.Equal(UserStatus.Active, user.Status);
    }
}
