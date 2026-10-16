namespace Contractra.Domain.Audit;

/// <summary>
/// Who performed the audited action (auth contract v1.5 §14.1, owner decision
/// R40; repository ADR 0006 E28). The enum names are the stored codes; they are
/// permanent once written to the append-only table.
/// </summary>
public enum AuditActorType
{
    /// <summary>A signed-in user; the event names the user.</summary>
    User,

    /// <summary>The system itself (startup seeding, operator commands, jobs): no user and no role.</summary>
    System
}
