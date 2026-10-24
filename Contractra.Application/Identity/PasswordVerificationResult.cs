namespace Contractra.Application.Identity;

/// <summary>
/// Mirrors <see cref="Microsoft.AspNetCore.Identity.PasswordVerificationResult"/>
/// so Application does not reference ASP.NET Core Identity directly (auth
/// contract v1.5 R33: a transparent rehash on <see cref="SuccessRehashNeeded"/>).
/// </summary>
public enum PasswordVerificationResult
{
    Failed,
    Success,
    SuccessRehashNeeded,
}
