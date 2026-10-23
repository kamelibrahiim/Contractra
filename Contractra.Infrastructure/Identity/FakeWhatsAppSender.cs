using System.Collections.Concurrent;
using Contractra.Application.Identity.Credentials;

namespace Contractra.Infrastructure.Identity;

/// <summary>One message the fake sender received. Test-only: the password is exposed here and nowhere else.</summary>
public sealed record FakeSentMessage(string Destination, string Username, string Password);

/// <summary>
/// The development/test sender (ADR 0007 §10; PS4a-BE-05). It records messages in memory for tests and replays
/// scripted outcomes. It never logs. <see cref="FakeSenderRegistration"/> refuses to register it outside
/// Development or Testing.
/// </summary>
public sealed class FakeWhatsAppSender : IWhatsAppSender
{
    private readonly ConcurrentQueue<FakeSentMessage> _sent = new();
    private readonly ConcurrentQueue<WhatsAppSendResult> _script = new();

    public bool IsConfigured => true;

    public IReadOnlyCollection<FakeSentMessage> Sent => _sent.ToArray();

    /// <summary>Queues outcomes for the next sends (first in, first out); after that every send is accepted.</summary>
    public void Script(params WhatsAppSendResult[] outcomes)
    {
        foreach (var outcome in outcomes)
        {
            _script.Enqueue(outcome);
        }
    }

    public Task<WhatsAppSendResult> SendAsync(
        string destination, string username, SensitivePassword password, CancellationToken cancellationToken = default)
    {
        _sent.Enqueue(new FakeSentMessage(destination, username, password.Reveal()));
        if (_script.TryDequeue(out var scripted))
        {
            return Task.FromResult(scripted);
        }

        // Unique across senders: provider message ids are unique per issuance in the database.
        var id = $"fake.{Guid.NewGuid():N}";
        return Task.FromResult(new WhatsAppSendResult(WhatsAppSendOutcome.Accepted, id));
    }
}

/// <summary>The sender used when WhatsApp is not configured: every create and reset answers 503.</summary>
public sealed class UnconfiguredWhatsAppSender : IWhatsAppSender
{
    public bool IsConfigured => false;

    public Task<WhatsAppSendResult> SendAsync(
        string destination, string username, SensitivePassword password, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("No WhatsApp sender is configured.");
}

public static class FakeSenderRegistration
{
    /// <summary>The fake sender may be registered only in Development or Testing; anywhere else this throws at startup.</summary>
    public static void EnsureAllowed(string environmentName)
    {
        if (!string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(environmentName, "Testing", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The fake WhatsApp sender cannot be registered in the '{environmentName}' environment (ADR 0007 §10).");
        }
    }
}
