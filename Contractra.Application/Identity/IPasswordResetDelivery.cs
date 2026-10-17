namespace Contractra.Application.Identity;

/// <summary>
/// Delivers a password-reset code to the user (auth contract v1.5 §10A). No
/// implementation is registered in Sprint 2: the issuing use case fails
/// closed with 503 when no channel is available, rather than pretending one
/// exists.
/// </summary>
public interface IPasswordResetDelivery
{
    /// <summary>The channel name recorded on the ticket, e.g. "email".</summary>
    string Channel { get; }

    /// <summary>Whether this channel can currently reach the given user.</summary>
    Task<bool> CanReachAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Sends the code. Called after the issuing transaction commits.</summary>
    Task SendAsync(Guid userId, string code, CancellationToken cancellationToken = default);
}
