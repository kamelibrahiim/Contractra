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
        if (Status != UserStatus.Active)
        {
            throw new InvalidOperationException($"Only an Active user can be disabled; current status is {Status}.");
        }

        Status = UserStatus.Disabled;
    }

    public void Reactivate()
    {
        if (Status != UserStatus.Disabled)
        {
            throw new InvalidOperationException($"Only a Disabled user can be reactivated; current status is {Status}.");
        }

        Status = UserStatus.Active;
    }
}
