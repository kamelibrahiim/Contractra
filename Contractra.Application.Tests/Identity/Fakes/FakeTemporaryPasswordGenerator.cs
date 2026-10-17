using Contractra.Application.Identity;

namespace Contractra.Application.Tests.Identity.Fakes;

internal sealed class FakeTemporaryPasswordGenerator : ITemporaryPasswordGenerator
{
    private int _count;

    public string Generate() => $"Temp{++_count}Aa1";
}
