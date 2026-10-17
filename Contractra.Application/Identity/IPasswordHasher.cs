namespace Contractra.Application.Identity;

/// <summary>
/// Computes and verifies password hashes (auth contract v1.5 R33). The
/// Domain never hashes or verifies a password; it only stores the hash
/// string this interface produces.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>Hashes a plaintext password that is not <see cref="Contractra.Domain.Identity.User.UnusablePasswordHash"/>.</summary>
    string Hash(string password);

    /// <summary>
    /// Verifies a plaintext password against a stored hash.
    /// <see cref="PasswordVerificationResult.SuccessRehashNeeded"/> means the
    /// password is correct but the stored hash should be replaced with a
    /// fresh one computed from the same password (R33).
    /// </summary>
    PasswordVerificationResult Verify(string hash, string password);
}
