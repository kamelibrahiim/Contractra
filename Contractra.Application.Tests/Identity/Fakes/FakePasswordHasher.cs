using Contractra.Application.Identity;

namespace Contractra.Application.Tests.Identity.Fakes;

/// <summary>A deterministic, reversible stand-in: "hash:{password}". Never used outside tests.</summary>
internal sealed class FakePasswordHasher : IPasswordHasher
{
    private const string Prefix = "hash:";

    /// <summary>Counts calls to <see cref="Hash"/>, so a test can prove a pre-lookup/pre-verification guard prevented any hashing of the new password.</summary>
    public int HashCallCount { get; private set; }

    public string Hash(string password)
    {
        HashCallCount++;
        return Prefix + password;
    }

    public PasswordVerificationResult Verify(string hash, string password) =>
        hash == Prefix + password ? PasswordVerificationResult.Success : PasswordVerificationResult.Failed;
}
