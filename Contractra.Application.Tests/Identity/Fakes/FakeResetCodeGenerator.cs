using System.Text;
using Contractra.Application.Identity;

namespace Contractra.Application.Tests.Identity.Fakes;

/// <summary>Deterministic, non-cryptographic stand-in: a fixed 32-byte hash per plaintext code.</summary>
internal sealed class FakeResetCodeGenerator : IResetCodeGenerator
{
    private int _count;

    public GeneratedResetCode Generate()
    {
        var code = $"CODE-{++_count:D4}-0000-0000";
        return new GeneratedResetCode(code, Hash(code));
    }

    public byte[] Hash(string submittedCode)
    {
        var normalized = submittedCode.Replace("-", "").Replace(" ", "").ToUpperInvariant();
        var bytes = Encoding.ASCII.GetBytes(normalized.PadRight(32, '0')[..32]);
        return bytes;
    }
}
