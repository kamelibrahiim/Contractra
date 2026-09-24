namespace Contractra.Domain.Identity;

public sealed class User
{
    public Guid Id { get; private set; }

    public UserStatus Status { get; private set; }

    private User()
    {
    }

    public User(UserStatus status)
        : this(Guid.CreateVersion7(), status)
    {
    }

    public User(Guid id, UserStatus status)
    {
        Id = id;
        Status = status;
    }
}
