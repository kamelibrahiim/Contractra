using Contractra.Application.Identity.Credentials;
using Contractra.Domain.Identity;
using Contractra.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Contractra.Infrastructure.Persistence.Identity;

/// <summary>
/// <see cref="ICredentialIssuanceStore"/> over EF Core. The sealed columns are read only when a row is loaded as
/// an entity for the worker; every other read here projects explicit columns (ADR 0007 §5).
/// </summary>
public sealed class CredentialIssuanceStore : ICredentialIssuanceStore
{
    private static readonly CredentialDeliveryStatus[] TerminalStatuses =
    [
        CredentialDeliveryStatus.Delivered, CredentialDeliveryStatus.Read, CredentialDeliveryStatus.Failed,
        CredentialDeliveryStatus.Superseded, CredentialDeliveryStatus.Cancelled,
    ];

    private readonly ContractraDbContext _dbContext;

    public CredentialIssuanceStore(ContractraDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public void Add(CredentialIssuance issuance) => _dbContext.CredentialIssuances.Add(issuance);

    public Task<CredentialIssuance?> FindOpenForUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        _dbContext.CredentialIssuances
            .Where(i => i.UserId == userId && !TerminalStatuses.Contains(i.Status))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<CredentialIssuanceSummary?> FindLatestSummaryForUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
        _dbContext.CredentialIssuances.AsNoTracking()
            .Where(i => i.UserId == userId)
            .OrderByDescending(i => i.IssuedAt).ThenByDescending(i => i.Id) // ids are time-ordered UUIDv7: a deterministic tie-break
            .Select(i => new CredentialIssuanceSummary(
                i.Id, i.UserId, i.Kind, i.Status, i.Attempts, i.IssuedAt, i.UpdatedAt, i.Destination, i.ConsentBasis, i.FailureCategory))
            .FirstOrDefaultAsync(cancellationToken);

    public Task<CredentialIssuance?> FindByIdAsync(Guid issuanceId, CancellationToken cancellationToken = default) =>
        _dbContext.CredentialIssuances.SingleOrDefaultAsync(i => i.Id == issuanceId, cancellationToken);

    public Task<CredentialIssuance?> FindByProviderMessageIdAsync(string providerMessageId, CancellationToken cancellationToken = default) =>
        _dbContext.CredentialIssuances.SingleOrDefaultAsync(i => i.ProviderMessageId == providerMessageId, cancellationToken);

    public Task<int> CountSinceAsync(Guid userId, DateTimeOffset since, CancellationToken cancellationToken = default) =>
        _dbContext.CredentialIssuances.AsNoTracking().CountAsync(i => i.UserId == userId && i.IssuedAt >= since, cancellationToken);

    public async Task<IReadOnlyList<CredentialIssuanceSummary>> ListSummariesForUserAsync(
        Guid userId, int limit, CancellationToken cancellationToken = default) =>
        await _dbContext.CredentialIssuances.AsNoTracking()
            .Where(i => i.UserId == userId)
            .OrderByDescending(i => i.IssuedAt).ThenByDescending(i => i.Id)
            .Take(limit)
            .Select(i => new CredentialIssuanceSummary(
                i.Id, i.UserId, i.Kind, i.Status, i.Attempts, i.IssuedAt, i.UpdatedAt, i.Destination, i.ConsentBasis, i.FailureCategory))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListDueIdsAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken = default) =>
        await _dbContext.CredentialIssuances.AsNoTracking()
            .Where(i => (i.Status == CredentialDeliveryStatus.Queued || i.Status == CredentialDeliveryStatus.Sending)
                && i.NextAttemptAt != null && i.NextAttemptAt <= now
                && (i.LeaseUntil == null || i.LeaseUntil < now))
            .OrderBy(i => i.NextAttemptAt)
            .Select(i => i.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListOpenIdsAsync(int limit, CancellationToken cancellationToken = default) =>
        await _dbContext.CredentialIssuances.AsNoTracking()
            .Where(i => !TerminalStatuses.Contains(i.Status))
            .OrderBy(i => i.IssuedAt)
            .Select(i => i.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListIdsWithSealedCopyAsync(CancellationToken cancellationToken = default) =>
        await _dbContext.CredentialIssuances.AsNoTracking()
            .Where(i => i.SealedBytes != null)
            .Select(i => i.Id)
            .ToListAsync(cancellationToken);

    public Task<bool> HasSignedInSinceAsync(Guid userId, DateTimeOffset since, CancellationToken cancellationToken = default) =>
        _dbContext.AuthSessions.AsNoTracking().AnyAsync(s => s.UserId == userId && s.CreatedAt >= since, cancellationToken);

    public async Task<CredentialIssuance?> TryClaimAsync(
        Guid issuanceId, string owner, DateTimeOffset now, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
    {
        var issuance = await _dbContext.CredentialIssuances.SingleOrDefaultAsync(i => i.Id == issuanceId, cancellationToken);
        if (issuance is null || !issuance.TryClaim(owner, now, leaseDuration))
        {
            return null;
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return issuance;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another worker (or a webhook) changed the row first.
            _dbContext.Entry(issuance).State = EntityState.Detached;
            return null;
        }
    }

    public Task SaveAsync(CancellationToken cancellationToken = default) => _dbContext.SaveChangesAsync(cancellationToken);

    public async Task<bool> TryRecordWebhookEventAsync(
        string providerMessageId, string status, DateTimeOffset providerTimestamp, string? errorCode, string? errorTitle,
        CancellationToken cancellationToken = default)
    {
        var entry = _dbContext.WhatsAppWebhookEvents.Add(new WhatsAppWebhookEvent
        {
            ProviderMessageId = providerMessageId,
            Status = status,
            ProviderTimestamp = providerTimestamp.ToUniversalTime(),
            ErrorCode = Truncate(errorCode, 64),
            ErrorTitle = Truncate(errorTitle, 256),
            ReceivedAt = DateTimeOffset.UtcNow,
        });

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (exception.GetBaseException() is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            entry.State = EntityState.Detached;
            return false;
        }
    }

    private static string? Truncate(string? value, int max) => value is null ? null : value.Length <= max ? value : value[..max];

}
