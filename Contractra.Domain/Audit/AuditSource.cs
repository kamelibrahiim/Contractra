namespace Contractra.Domain.Audit;

/// <summary>
/// Where the audited action came from (logical model §15.4; repository ADR 0006
/// §5.3, owner decision R46). The enum names are the stored codes; they are
/// permanent once written to the append-only table.
/// </summary>
public enum AuditSource
{
    /// <summary>The Admin application.</summary>
    Web,

    /// <summary>The Employee application, on any platform (owner decision R46; replaces the old "PWA").</summary>
    EmployeeApp,

    /// <summary>Records synchronised from offline drafts.</summary>
    OfflineSync,

    /// <summary>Historical import.</summary>
    Import,

    /// <summary>Startup seeding, operator commands and background jobs.</summary>
    SystemJob
}
