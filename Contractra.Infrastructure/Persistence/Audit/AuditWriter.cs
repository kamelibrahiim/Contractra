using Contractra.Application.Audit;
using Contractra.Domain.Audit;

namespace Contractra.Infrastructure.Persistence.Audit;

/// <summary>
/// Stages an audit event in the caller's scoped <see cref="ContractraDbContext"/>
/// (repository ADR 0006 §6). It only adds the entity: it never calls SaveChanges
/// and never opens, commits or rolls back a transaction, so the event is written by
/// the caller's single save and commits or rolls back with the business change.
/// </summary>
public sealed class AuditWriter : IAuditWriter
{
    private readonly ContractraDbContext _dbContext;

    public AuditWriter(ContractraDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public void Stage(AuditEvent auditEvent)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        _dbContext.AuditEvents.Add(auditEvent);
    }
}
