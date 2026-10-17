using Contractra.Domain.Audit;

namespace Contractra.Application.Identity.UseCases;

/// <summary>
/// Auth contract v1.5 §5.5. <paramref name="SessionId"/> is the access
/// token's <c>sid</c> claim; <paramref name="Source"/> is the audit source
/// for the app the token's <c>app</c> claim names (Web for Admin, EmployeeApp
/// for Employee) - resolved by the caller (Slice 6), not re-derived here.
/// </summary>
public sealed record ChangePasswordRequest(
    Guid UserId,
    Guid SessionId,
    string CurrentPassword,
    string NewPassword,
    string CorrelationId,
    AuditSource Source);
