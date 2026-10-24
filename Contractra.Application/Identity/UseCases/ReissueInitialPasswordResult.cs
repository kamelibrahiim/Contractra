namespace Contractra.Application.Identity.UseCases;

public sealed record ReissueInitialPasswordResult(string TemporaryPassword, DateTimeOffset TemporaryPasswordExpiresAt);
