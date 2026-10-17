using Contractra.Application.Identity;

namespace Contractra.Application.Tests.Identity.Fakes;

internal sealed class FakePasswordResetDelivery : IPasswordResetDelivery
{
    private readonly bool _canReach;

    public FakePasswordResetDelivery(string channel = "operator", bool canReach = true)
    {
        Channel = channel;
        _canReach = canReach;
    }

    public string Channel { get; }

    public List<(Guid UserId, string Code)> Sent { get; } = new();

    /// <summary>Runs when a send is attempted, before it succeeds or throws - lets a test observe state at send time.</summary>
    public Action? OnSend { get; set; }

    /// <summary>When set, a send attempt throws this after recording itself.</summary>
    public Exception? ThrowOnSend { get; set; }

    public Task<bool> CanReachAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult(_canReach);

    public Task SendAsync(Guid userId, string code, CancellationToken cancellationToken = default)
    {
        OnSend?.Invoke();
        Sent.Add((userId, code));
        return ThrowOnSend is null ? Task.CompletedTask : Task.FromException(ThrowOnSend);
    }
}
