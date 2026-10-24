using System.Security.Cryptography;

namespace Contractra.Application.Identity.Auth;

/// <summary>
/// A hash of a random value nobody knows, computed once per process with the real
/// hasher. Login verifies against it for an unknown or disabled account and for an
/// account without a usable password, so timing does not reveal which case applied
/// (contract §5.1, owner decision R42).
/// </summary>
public sealed class DummyPasswordHash
{
    public DummyPasswordHash(IPasswordHasher hasher)
    {
        ArgumentNullException.ThrowIfNull(hasher);
        Value = hasher.Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
    }

    public string Value { get; }
}
