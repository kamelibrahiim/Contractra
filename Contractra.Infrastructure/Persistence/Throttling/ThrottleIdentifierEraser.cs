using Contractra.Application.Identity.Auth;
using Microsoft.EntityFrameworkCore;

namespace Contractra.Infrastructure.Persistence.Throttling;

/// <summary>
/// Clears one identifier's layer 3 row in the caller's scoped
/// <see cref="ContractraDbContext"/> (ADR 0006 §6), unlike every
/// <see cref="IAuthThrottle"/> admission check, which deliberately runs in its
/// own short transaction via <see cref="AuthThrottleStore"/> so it is never
/// held open behind a password hash. This operator-relief path has no
/// admission decision to protect with row locking - it is an unconditional
/// delete-if-present - so it can safely run inside the use case's own
/// transaction instead, letting the delete and its audit event commit or roll
/// back together.
/// </summary>
public sealed class ThrottleIdentifierEraser : IThrottleIdentifierEraser
{
    private readonly ContractraDbContext _dbContext;

    public ThrottleIdentifierEraser(ContractraDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> ClearIdentifierAsync(byte[] identifierKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifierKey);

        var deleted = await _dbContext.AuthThrottleCounters
            .Where(c => c.Scope == AuthThrottleScopes.Identifier && c.KeyHash == identifierKey)
            .ExecuteDeleteAsync(cancellationToken);

        return deleted > 0;
    }
}
