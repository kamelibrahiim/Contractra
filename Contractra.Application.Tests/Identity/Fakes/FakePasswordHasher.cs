using Contractra.Application.Identity;

namespace Contractra.Application.Tests.Identity.Fakes;

/// <summary>A deterministic, reversible stand-in: "hash:{password}". Never used outside tests.</summary>
internal sealed class FakePasswordHasher : IPasswordHasher
{
    private const string Prefix = "hash:";

    public string Hash(string password) => Prefix + password;

    public PasswordVerificationResult Verify(string hash, string password) =>
        hash == Prefix + password ? PasswordVerificationResult.Success : PasswordVerificationResult.Failed;
}
