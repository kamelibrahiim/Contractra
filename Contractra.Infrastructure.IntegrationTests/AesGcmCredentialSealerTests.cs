using System.Security.Cryptography;
using System.Text;
using Contractra.Application.Identity.Credentials;
using Contractra.Infrastructure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

namespace Contractra.Infrastructure.IntegrationTests;

/// <summary>The real sealer (ADR 0007 §4-§5): AES-256-GCM bound to the row, a kid-tagged key ring, fail-closed loading. No database.</summary>
public class AesGcmCredentialSealerTests
{
    private const string Password = "Zx9mQ2vL7kRt";

    private static CredentialSealingKeyRing Ring(string active, params string[] ids) =>
        CredentialSealingKeyRing.FromKeys(active, ids.ToDictionary(id => id, _ => RandomNumberGenerator.GetBytes(32)));

    private static SealContext NewContext() => new(Guid.CreateVersion7(), Guid.CreateVersion7());

    [Fact]
    public void SealThenOpen_RoundTripsThePassword_ForTheSameRow()
    {
        var sealer = new AesGcmCredentialSealer(Ring("k1", "k1"));
        var context = NewContext();

        var sealedSecret = sealer.Seal(new SensitivePassword(Password), context);

        Assert.Equal("k1", sealedSecret.KeyId);
        Assert.Equal(Password, sealer.Open(sealedSecret, context).Reveal());
    }

    [Fact]
    public void TheSealedBytes_NeverContainThePasswordInTheClear()
    {
        var sealer = new AesGcmCredentialSealer(Ring("k1", "k1"));

        var sealedSecret = sealer.Seal(new SensitivePassword(Password), NewContext());

        var plaintext = Encoding.UTF8.GetBytes(Password);
        Assert.False(sealedSecret.Bytes.AsSpan().IndexOf(plaintext) >= 0);
        Assert.Equal(Password.Length + 16, sealedSecret.Bytes.Length); // ciphertext || 128-bit tag
        Assert.Equal(12, sealedSecret.Nonce.Length);
    }

    [Fact]
    public void EachSealUsesAFreshNonce_SoEqualPasswordsGiveDifferentCiphertext()
    {
        var sealer = new AesGcmCredentialSealer(Ring("k1", "k1"));
        var context = NewContext();

        var first = sealer.Seal(new SensitivePassword(Password), context);
        var second = sealer.Seal(new SensitivePassword(Password), context);

        Assert.NotEqual(first.Nonce, second.Nonce);
        Assert.NotEqual(first.Bytes, second.Bytes);
    }

    [Fact]
    public void ACopyMovedToAnotherIssuanceOrUser_FailsAuthentication()
    {
        var sealer = new AesGcmCredentialSealer(Ring("k1", "k1"));
        var context = NewContext();
        var sealedSecret = sealer.Seal(new SensitivePassword(Password), context);

        var otherIssuance = Assert.Throws<SealOpenException>(() => sealer.Open(sealedSecret, context with { IssuanceId = Guid.CreateVersion7() }));
        var otherUser = Assert.Throws<SealOpenException>(() => sealer.Open(sealedSecret, context with { UserId = Guid.CreateVersion7() }));

        Assert.Equal(SealOpenFailure.AuthenticationFailed, otherIssuance.Failure);
        Assert.Equal(SealOpenFailure.AuthenticationFailed, otherUser.Failure);
    }

    [Fact]
    public void ATamperedCiphertextNonceOrKeyId_FailsAuthentication()
    {
        var ring = CredentialSealingKeyRing.FromKeys("k1", new Dictionary<string, byte[]>
        {
            ["k1"] = RandomNumberGenerator.GetBytes(32),
            ["k2"] = RandomNumberGenerator.GetBytes(32),
        });
        var sealer = new AesGcmCredentialSealer(ring);
        var context = NewContext();
        var sealedSecret = sealer.Seal(new SensitivePassword(Password), context);

        var flippedByte = (byte[])sealedSecret.Bytes.Clone();
        flippedByte[0] ^= 0x01;
        var flippedNonce = (byte[])sealedSecret.Nonce.Clone();
        flippedNonce[0] ^= 0x01;

        foreach (var tampered in new[]
        {
            sealedSecret with { Bytes = flippedByte },
            sealedSecret with { Nonce = flippedNonce },
            sealedSecret with { KeyId = "k2" }, // a different key id: other key and other associated data
            sealedSecret with { Bytes = sealedSecret.Bytes[..^1] },
        })
        {
            var exception = Assert.Throws<SealOpenException>(() => sealer.Open(tampered, context));
            Assert.Equal(SealOpenFailure.AuthenticationFailed, exception.Failure);
        }
    }

    [Fact]
    public void AnUnknownKeyId_IsKeyUnavailable()
    {
        var sealer = new AesGcmCredentialSealer(Ring("k1", "k1"));
        var context = NewContext();
        var sealedSecret = sealer.Seal(new SensitivePassword(Password), context);

        var exception = Assert.Throws<SealOpenException>(() => sealer.Open(sealedSecret with { KeyId = "gone" }, context));

        Assert.Equal(SealOpenFailure.KeyUnavailable, exception.Failure);
    }

    [Fact]
    public void Rotation_NewCopiesUseTheNewActiveKey_AndOldCopiesStillOpenWhileTheOldKeyIsInTheRing()
    {
        var oldKey = RandomNumberGenerator.GetBytes(32);
        var newKey = RandomNumberGenerator.GetBytes(32);
        var before = new AesGcmCredentialSealer(CredentialSealingKeyRing.FromKeys("k1", new Dictionary<string, byte[]> { ["k1"] = oldKey }));
        var context = NewContext();
        var oldCopy = before.Seal(new SensitivePassword(Password), context);

        var rotated = new AesGcmCredentialSealer(CredentialSealingKeyRing.FromKeys(
            "k2", new Dictionary<string, byte[]> { ["k1"] = oldKey, ["k2"] = newKey }));
        var newCopy = rotated.Seal(new SensitivePassword(Password), context);

        Assert.Equal("k2", newCopy.KeyId);
        Assert.Equal(Password, rotated.Open(oldCopy, context).Reveal());
        Assert.Equal(Password, rotated.Open(newCopy, context).Reveal());

        // After the old key is destroyed, an old copy (and a backup of it) can no longer be opened.
        var destroyed = new AesGcmCredentialSealer(CredentialSealingKeyRing.FromKeys("k2", new Dictionary<string, byte[]> { ["k2"] = newKey }));
        Assert.Equal(SealOpenFailure.KeyUnavailable, Assert.Throws<SealOpenException>(() => destroyed.Open(oldCopy, context)).Failure);
    }

    [Fact]
    public void WithoutAnActiveKey_TheSealerIsUnavailable_AndSealingThrows()
    {
        var sealer = new AesGcmCredentialSealer(CredentialSealingKeyRing.Empty);

        Assert.False(sealer.IsAvailable);
        Assert.Throws<InvalidOperationException>(() => sealer.Seal(new SensitivePassword(Password), NewContext()));
    }

    [Fact]
    public void TheWrapperTypes_HideTheirContentInTextForm()
    {
        var password = new SensitivePassword(Password);
        var sealedSecret = new SealedSecret([1, 2, 3], [4, 5, 6], "k1");

        Assert.DoesNotContain(Password, password.ToString());
        Assert.DoesNotContain(Password, $"{password}");
        Assert.DoesNotContain("1, 2, 3", sealedSecret.ToString());
        Assert.False(password.Equals(new SensitivePassword(Password))); // no value equality that could be probed
    }

    // ---- key ring loading (fail closed) ---------------------------------------------------------------------

    [Fact]
    public void Load_WithoutAnyConfiguration_GivesAnEmptyRing_SoCreateAndResetAnswer503()
    {
        var ring = CredentialSealingKeyRing.Load(new Settings());

        Assert.False(ring.HasActiveKey);
    }

    [Fact]
    public void Load_ReadsTheActiveKeyAndTheDecryptOnlyKeys()
    {
        var ring = CredentialSealingKeyRing.Load(new Settings(
            ("CredentialSealing:ActiveKeyId", "k2"),
            ("CredentialSealing:Keys:k1", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))),
            ("CredentialSealing:Keys:k2", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)))));

        Assert.True(ring.HasActiveKey);
        Assert.Equal("k2", ring.ActiveKeyId);
        Assert.NotNull(ring.Find("k1"));
        Assert.NotNull(ring.Find("k2"));
        Assert.Null(ring.Find("k3"));
    }

    [Theory]
    [InlineData("not base64!!")]
    [InlineData("AAAA")] // far too short
    public void Load_RefusesAMalformedOrShortKey_AtStartup(string material)
    {
        var settings = new Settings(("CredentialSealing:ActiveKeyId", "k1"), ("CredentialSealing:Keys:k1", material));

        Assert.Throws<InvalidOperationException>(() => CredentialSealingKeyRing.Load(settings));
    }

    [Fact]
    public void Load_RefusesAnActiveKeyIdThatIsNotInTheRing_AndABadKeyId()
    {
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        Assert.Throws<InvalidOperationException>(() => CredentialSealingKeyRing.Load(
            new Settings(("CredentialSealing:ActiveKeyId", "missing"), ("CredentialSealing:Keys:k1", key))));
        Assert.Throws<InvalidOperationException>(() => CredentialSealingKeyRing.Load(
            new Settings(("CredentialSealing:ActiveKeyId", "bad kid"), ("CredentialSealing:Keys:bad kid", key))));
    }

    [Fact]
    public void TheFakeSender_CannotBeRegisteredOutsideDevelopmentOrTesting()
    {
        FakeSenderRegistration.EnsureAllowed("Development");
        FakeSenderRegistration.EnsureAllowed("Testing");

        Assert.Throws<InvalidOperationException>(() => FakeSenderRegistration.EnsureAllowed("Production"));
        Assert.Throws<InvalidOperationException>(() => FakeSenderRegistration.EnsureAllowed("Staging"));
        Assert.Throws<InvalidOperationException>(() => FakeSenderRegistration.EnsureAllowed(string.Empty));
    }

    /// <summary>A flat key/value IConfiguration with real section children, so the ring can enumerate its keys.</summary>
    private sealed class Settings : IConfiguration
    {
        private readonly Dictionary<string, string?> _values;

        public Settings(params (string Key, string Value)[] values) =>
            _values = values.ToDictionary(v => v.Key, v => (string?)v.Value, StringComparer.OrdinalIgnoreCase);

        public string? this[string key]
        {
            get => _values.GetValueOrDefault(key);
            set => throw new NotSupportedException();
        }

        public IConfigurationSection GetSection(string key) => new Section(this, key);

        public IEnumerable<IConfigurationSection> GetChildren() => [];

        public IChangeToken GetReloadToken() => new CancellationChangeToken(CancellationToken.None);

        private sealed class Section : IConfigurationSection
        {
            private readonly Settings _root;

            public Section(Settings root, string path) => (_root, Path) = (root, path);

            public string Path { get; }

            public string Key => Path.Split(':').Last();

            public string? Value
            {
                get => _root[Path];
                set => throw new NotSupportedException();
            }

            public string? this[string key]
            {
                get => _root[$"{Path}:{key}"];
                set => throw new NotSupportedException();
            }

            public IConfigurationSection GetSection(string key) => new Section(_root, $"{Path}:{key}");

            public IEnumerable<IConfigurationSection> GetChildren()
            {
                var prefix = Path + ":";
                return _root._values.Keys
                    .Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .Select(k => k[prefix.Length..].Split(':')[0])
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(child => (IConfigurationSection)new Section(_root, prefix + child));
            }

            public IChangeToken GetReloadToken() => _root.GetReloadToken();
        }
    }
}
