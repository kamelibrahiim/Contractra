namespace Contractra.Application.Identity;

/// <summary>A use case's outcome: either a value, or an error with its contract code.</summary>
public sealed class UseCaseResult<T>
{
    public T? Value { get; }

    public UseCaseError? Error { get; }

    public bool IsSuccess => Error is null;

    private UseCaseResult(T? value, UseCaseError? error)
    {
        Value = value;
        Error = error;
    }

    public static UseCaseResult<T> Success(T value) => new(value, null);

    public static UseCaseResult<T> Failure(UseCaseError error) => new(default, error);

    public static UseCaseResult<T> Failure(string code, string message) => new(default, new UseCaseError(code, message));
}

/// <summary>A use case's outcome with no value: either success, or an error with its contract code.</summary>
public sealed class UseCaseResult
{
    public UseCaseError? Error { get; }

    public bool IsSuccess => Error is null;

    private UseCaseResult(UseCaseError? error)
    {
        Error = error;
    }

    public static UseCaseResult Success() => new(null);

    public static UseCaseResult Failure(UseCaseError error) => new(error);

    public static UseCaseResult Failure(string code, string message) => new(new UseCaseError(code, message));
}
