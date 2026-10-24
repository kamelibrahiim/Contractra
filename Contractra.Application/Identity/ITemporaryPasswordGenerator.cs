namespace Contractra.Application.Identity;

/// <summary>
/// Generates the random temporary password shown once at creation (§9.1) and
/// initial re-issue (§9.8) — auth contract v1.5 §10, owner decision R2,
/// engineering decision E4: 12 characters, at least one upper-case letter,
/// one lower-case letter and one digit, from a cryptographically secure
/// generator.
/// </summary>
public interface ITemporaryPasswordGenerator
{
    string Generate();
}
