using Contractra.Domain.Audit;

namespace Contractra.Application.Audit;

/// <summary>
/// Stages an audit event in the caller's unit of work (business rule ح-186;
/// auth contract v1.5 §14.2; repository ADR 0006 §6). It only stages: it never
/// saves and never opens or commits a transaction. The caller owns the transaction
/// (opened before its first write), saves once, and commits through a single exit;
/// the business change and its audit event then commit or roll back together. After
/// any failure the unit of work, including its DbContext, is discarded.
/// </summary>
public interface IAuditWriter
{
    void Stage(AuditEvent auditEvent);
}
