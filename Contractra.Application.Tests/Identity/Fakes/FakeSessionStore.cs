using Contractra.Application.Identity;
using Contractra.Domain.Identity;

namespace Contractra.Application.Tests.Identity.Fakes;

internal sealed class FakeSessionStore : ISessionStore
{
    private readonly Dictionary<Guid, AuthSession> _sessions = new();

    public List<(Guid UserId, SessionRevokeReason Reason, Guid? ExceptSessionId)> RevokeAllCalls { get; } = new();

    public void Seed(AuthSession session) => _sessions[session.Id] = session;

    public Task<AuthSession?> FindByIdAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_sessions.GetValueOrDefault(sessionId));

    public Task<int> RevokeAllActiveAsync(
        Guid userId,
        SessionRevokeReason reason,
        DateTimeOffset revokedAt,
        Guid? exceptSessionId = null,
        CancellationToken cancellationToken = default)
    {
        RevokeAllCalls.Add((userId, reason, exceptSessionId));

        var revoked = 0;
        foreach (var session in _sessions.Values)
        {
            if (session.UserId == userId && session.RevokedAt is null && session.Id != exceptSessionId)
            {
                session.Revoke(reason, revokedAt);
                revoked++;
            }
        }

        return Task.FromResult(revoked);
    }
}
