namespace Contractra.Application.Identity;

/// <summary>
/// Generates and hashes the password-reset code (auth contract v1.5 §10A,
/// owner decision R44, engineering decision E1): 16 characters from the
/// Crockford base-32 alphabet (80 bits), shown in four groups of four.
/// Hyphens, spaces and case are ignored on input.
/// </summary>
public interface IResetCodeGenerator
{
    GeneratedResetCode Generate();

    /// <summary>
    /// Normalizes (strips hyphens/spaces, upper-cases) and hashes a
    /// user-submitted code the same way as <see cref="Generate"/>, so the
    /// two hashes can be compared.
    /// </summary>
    byte[] Hash(string submittedCode);
}
