namespace Contractra.Domain.Identity;

public sealed class User
{
    public Guid Id { get; private set; }

    public LoginIdentifier LoginIdentifier { get; private set; }

    public UserStatus Status { get; private set; }

    private User()
    {
    }

    public User(LoginIdentifier loginIdentifier, UserStatus status)
        : this(Guid.CreateVersion7(), loginIdentifier, status)
    {
    }

    public User(Guid id, LoginIdentifier loginIdentifier, UserStatus status)
    {
        Id = id;
        LoginIdentifier = loginIdentifier;
        Status = status;
    }

    public void Disable()
    {
        Status = UserStatus.Disabled;
    }

    public void Reactivate()
    {
        Status = UserStatus.Active;
    }
}
