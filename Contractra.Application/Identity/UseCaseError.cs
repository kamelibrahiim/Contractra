namespace Contractra.Application.Identity;

/// <summary>
/// A failure a Slice 4 use case reports to its caller, carrying the contract
/// §12 error <c>code</c> so Slice 6/7 endpoints can map it directly to a
/// ProblemDetails response without re-deriving it.
/// </summary>
public sealed record UseCaseError(string Code, string Message)
{
    public override string ToString() => $"{Code}: {Message}";
}
