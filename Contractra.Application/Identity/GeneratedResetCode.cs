namespace Contractra.Application.Identity;

/// <summary>
/// A newly generated password-reset code: the plaintext form to deliver once
/// (auth contract v1.5 §10A) and the SHA-256 hash to store on the ticket.
/// Only <see cref="Hash"/> is ever persisted.
/// </summary>
public sealed record GeneratedResetCode(string Code, byte[] Hash);
