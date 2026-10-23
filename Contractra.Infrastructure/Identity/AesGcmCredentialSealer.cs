using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Contractra.Application.Identity.Credentials;
using Microsoft.Extensions.Configuration;

namespace Contractra.Infrastructure.Identity;

/// <summary>
/// The credential-sealing key ring (ADR 0007 §4-§5), modelled on the JWT key ring: <c>CredentialSealing:ActiveKeyId</c>
/// names the one active key; <c>CredentialSealing:Keys:&lt;kid&gt;</c> holds each key as base64 (the active key and any
/// decrypt-only keys). The keys come from the environment's secret store (user-secrets in development), never from
/// the application database, source or committed configuration. No ring or no active key means the sealer is
/// <i>unavailable</i> (create and reset answer 503); a malformed key stops startup. Key values are never logged.
/// </summary>
public sealed partial class CredentialSealingKeyRing
{
    public const int KeyBytes = 32;
    public const string Section = "CredentialSealing";

    private readonly IReadOnlyDictionary<string, byte[]> _keys;

    private CredentialSealingKeyRing(string? activeKeyId, IReadOnlyDictionary<string, byte[]> keys)
    {
        ActiveKeyId = activeKeyId;
        _keys = keys;
    }

    public string? ActiveKeyId { get; }

    public bool HasActiveKey => ActiveKeyId is not null && _keys.ContainsKey(ActiveKeyId);

    public byte[]? Find(string keyId) => _keys.TryGetValue(keyId, out var key) ? key : null;

    public static CredentialSealingKeyRing Empty { get; } = new(null, new Dictionary<string, byte[]>());

    public static CredentialSealingKeyRing FromKeys(string activeKeyId, IReadOnlyDictionary<string, byte[]> keys) => new(activeKeyId, keys);

    public static CredentialSealingKeyRing Load(IConfiguration configuration)
    {
        var section = configuration.GetSection(Section);
        var keys = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var entry in section.GetSection("Keys").GetChildren())
        {
            if (!KeyIdPattern().IsMatch(entry.Key))
            {
                throw new InvalidOperationException("A sealing key id must be 1-64 characters from A-Z a-z 0-9 - _ . (ADR 0007).");
            }

            byte[] material;
            try
            {
                material = Convert.FromBase64String(entry.Value ?? string.Empty);
            }
            catch (FormatException)
            {
                throw new InvalidOperationException($"Sealing key '{entry.Key}' is not valid base64.");
            }

            if (material.Length != KeyBytes)
            {
                throw new InvalidOperationException($"Sealing key '{entry.Key}' must be exactly 256 bits (ADR 0007).");
            }

            keys[entry.Key] = material;
        }

        var active = section["ActiveKeyId"];
        if (string.IsNullOrWhiteSpace(active))
        {
            return new CredentialSealingKeyRing(null, keys);
        }

        if (!keys.ContainsKey(active))
        {
            throw new InvalidOperationException("CredentialSealing:ActiveKeyId names a key that is not in the ring.");
        }

        return new CredentialSealingKeyRing(active, keys);
    }

    [GeneratedRegex("^[A-Za-z0-9._-]{1,64}$")]
    private static partial Regex KeyIdPattern();
}

/// <summary>
/// AES-256-GCM sealer (ADR 0007 §4): a fresh random 96-bit nonce per record; associated data binds the ciphertext
/// to its issuance, user and key id so a copy moved to another row fails to open. Stored bytes are
/// <c>ciphertext || tag</c>.
/// </summary>
public sealed class AesGcmCredentialSealer : ICredentialSealing, ICredentialOpening
{
    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private static readonly byte[] Purpose = Encoding.ASCII.GetBytes("contractra/credential-seal/v1");

    private readonly CredentialSealingKeyRing _ring;

    public AesGcmCredentialSealer(CredentialSealingKeyRing ring)
    {
        _ring = ring;
    }

    public bool IsAvailable => _ring.HasActiveKey;

    public SealedSecret Seal(SensitivePassword password, SealContext context)
    {
        if (!_ring.HasActiveKey)
        {
            throw new InvalidOperationException("No active credential-sealing key is configured.");
        }

        var keyId = _ring.ActiveKeyId!;
        var key = _ring.Find(keyId)!;
        var plaintext = Encoding.UTF8.GetBytes(password.Reveal());
        try
        {
            var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[TagBytes];
            using var aes = new AesGcm(key, TagBytes);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, AssociatedData(context, keyId));

            var bytes = new byte[ciphertext.Length + TagBytes];
            ciphertext.CopyTo(bytes, 0);
            tag.CopyTo(bytes, ciphertext.Length);
            return new SealedSecret(bytes, nonce, keyId);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public SensitivePassword Open(SealedSecret sealedSecret, SealContext context)
    {
        var key = _ring.Find(sealedSecret.KeyId) ?? throw new SealOpenException(SealOpenFailure.KeyUnavailable);
        if (sealedSecret.Bytes.Length <= TagBytes || sealedSecret.Nonce.Length != NonceBytes)
        {
            throw new SealOpenException(SealOpenFailure.AuthenticationFailed);
        }

        var cipherLength = sealedSecret.Bytes.Length - TagBytes;
        var plaintext = new byte[cipherLength];
        try
        {
            using var aes = new AesGcm(key, TagBytes);
            aes.Decrypt(
                sealedSecret.Nonce,
                sealedSecret.Bytes.AsSpan(0, cipherLength),
                sealedSecret.Bytes.AsSpan(cipherLength, TagBytes),
                plaintext,
                AssociatedData(context, sealedSecret.KeyId));
            return new SensitivePassword(Encoding.UTF8.GetString(plaintext));
        }
        catch (CryptographicException)
        {
            throw new SealOpenException(SealOpenFailure.AuthenticationFailed);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static byte[] AssociatedData(SealContext context, string keyId)
    {
        var key = Encoding.UTF8.GetBytes(keyId);
        var data = new byte[Purpose.Length + 1 + 16 + 16 + key.Length];
        var offset = 0;
        Purpose.CopyTo(data, offset);
        offset += Purpose.Length + 1; // a zero separator byte
        context.IssuanceId.TryWriteBytes(data.AsSpan(offset, 16));
        offset += 16;
        context.UserId.TryWriteBytes(data.AsSpan(offset, 16));
        offset += 16;
        key.CopyTo(data, offset);
        return data;
    }
}
