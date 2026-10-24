namespace Contractra.Application.Identity.UseCases;

/// <summary>
/// <paramref name="DeliveryFailed"/> is true when the code could not be handed
/// to the channel after the commit (contract §9.6 step 6): the ticket is valid
/// until <paramref name="ExpiresAt"/> but the user has no code.
/// </summary>
public sealed record IssuePasswordResetResult(string Channel, DateTimeOffset ExpiresAt, bool DeliveryFailed = false);
