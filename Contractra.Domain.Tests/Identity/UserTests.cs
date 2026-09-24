using Contractra.Domain.Identity;

namespace Contractra.Domain.Tests.Identity;

public class UserTests
{
    [Theory]
    [InlineData(UserStatus.Active)]
    [InlineData(UserStatus.Disabled)]
    public void Constructor_UsesTheExplicitlySuppliedStatus(UserStatus status)
    {
        var user = new User(status);

        Assert.Equal(status, user.Status);
    }

    [Fact]
    public void Constructor_GeneratesAVersion7Guid_WhenNoIdIsProvided()
    {
        var user = new User(UserStatus.Active);

        Assert.Equal('7', user.Id.ToString()[14]);
    }

    [Fact]
    public void Constructor_KeepsAnExplicitlySuppliedId()
    {
        var id = Guid.CreateVersion7();

        var user = new User(id, UserStatus.Active);

        Assert.Equal(id, user.Id);
    }

    [Fact]
    public void User_HasNoDeleteMethod()
    {
        var methodNames = typeof(User).GetMethods().Select(m => m.Name);

        Assert.DoesNotContain(methodNames, name => name.Contains("Delete", StringComparison.OrdinalIgnoreCase));
    }
}
