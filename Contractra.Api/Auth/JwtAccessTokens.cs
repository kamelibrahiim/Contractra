using System.Security.Claims;
using Contractra.Application.Common;
using Contractra.Application.Identity.Auth;
using Contractra.Domain.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Contractra.Api.Auth;

/// <summary>Claim names of contract §3.2.</summary>
public static class AccessTokenClaims
{
    public const string Subject = "sub";
    public const string Session = "sid";
    public const string App = "app";
    public const string SecurityVersion = "sv";
    public const string Permission = "perm";
    public const string Role = "role";
}

/// <summary>
/// Issues the contract §3.2 access token: HS256 with the active key (header <c>kid</c>),
/// <c>nbf = iat</c>, <c>exp = iat + 30 min</c>, configured <c>iss</c>/<c>aud</c>, and the
/// claims sub, sid, app, sv, perm, role. Times come from the application clock and are
/// truncated to whole seconds, as JWT times are.
/// </summary>
public sealed class JwtAccessTokenIssuer : IAccessTokenIssuer
{
    private readonly JwtKeyRing _ring;
    private readonly JsonWebTokenHandler _handler = new() { SetDefaultTimesOnTokenCreation = false };

    public JwtAccessTokenIssuer(JwtKeyRing ring)
    {
        _ring = ring;
    }

    public IssuedAccessToken Issue(AccessTokenSubject subject, DateTimeOffset issuedAt)
    {
        ArgumentNullException.ThrowIfNull(subject);
        var iat = DateTimeOffset.FromUnixTimeSeconds(issuedAt.ToUnixTimeSeconds());
        var expires = iat + AuthSessionPolicy.AccessTokenLifetime;

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _ring.Issuer,
            Audience = _ring.Audience,
            IssuedAt = iat.UtcDateTime,
            NotBefore = iat.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(_ring.ActiveKey, SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                [AccessTokenClaims.Subject] = subject.UserId.ToString("D"),
                [AccessTokenClaims.Session] = subject.SessionId.ToString("D"),
                [AccessTokenClaims.App] = AppAccess.Code(subject.App),
                [AccessTokenClaims.SecurityVersion] = subject.SecurityVersion,
                [AccessTokenClaims.Permission] = subject.Permissions.ToArray(),
                [AccessTokenClaims.Role] = subject.Roles.ToArray(),
            },
        });

        return new IssuedAccessToken(token, expires);
    }
}

/// <summary>
/// Validation of every presented access token (contract §3.2, E9). A token failing any
/// check is treated as absent (the endpoint then answers 401 auth.unauthenticated):
/// algorithm pinned to HS256 (never read from the token or the client); signature under
/// the key named by <c>kid</c>, which must be in the ring; iss and aud equal to the
/// configured values; exp and nbf required and checked against the application clock with
/// at most 30 seconds of skew; sub, sid, app, sv present and well-formed; then the one
/// indexed security-version lookup (§3.5), which also reads must_change_password.
/// </summary>
public static class JwtValidation
{
    public static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

    public static void Configure(JwtBearerOptions options, JwtKeyRing ring, IClock clock)
    {
        options.MapInboundClaims = false;
        options.RequireHttpsMetadata = false; // no metadata endpoint is used: keys come from the ring.
        options.IncludeErrorDetails = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            RequireSignedTokens = true,
            ValidateIssuerSigningKey = true,
            TryAllIssuerSigningKeys = false,
            IssuerSigningKeyResolver = (_, _, kid, _) => ring.Resolve(kid),
            ValidateIssuer = true,
            ValidIssuer = ring.Issuer,
            ValidateAudience = true,
            ValidAudience = ring.Audience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = ClockSkew,
            LifetimeValidator = (notBefore, expires, _, _) => IsWithinLifetime(notBefore, expires, clock.UtcNow),
            NameClaimType = AccessTokenClaims.Subject,
            RoleClaimType = AccessTokenClaims.Role,
        };

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = OnTokenValidatedAsync,
            OnAuthenticationFailed = context =>
            {
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Contractra.Api.Auth.Jwt");
                // Only the failure type: never the token or anything taken from it.
                logger.LogInformation("Auth.TokenRejected Reason={Reason}", context.Exception.GetType().Name);
                return Task.CompletedTask;
            },
            OnChallenge = async context =>
            {
                context.HandleResponse();
                await Http.Problems.WriteCodeAsync(context.HttpContext, AuthErrorCodes.Unauthenticated);
            },
        };
    }

    public static bool IsWithinLifetime(DateTime? notBefore, DateTime? expires, DateTimeOffset now)
    {
        if (notBefore is not { } nbf || expires is not { } exp)
        {
            return false;
        }

        var utcNow = now.UtcDateTime;
        return utcNow + ClockSkew >= DateTime.SpecifyKind(nbf, DateTimeKind.Utc)
            && utcNow - ClockSkew < DateTime.SpecifyKind(exp, DateTimeKind.Utc);
    }

    private static async Task OnTokenValidatedAsync(TokenValidatedContext context)
    {
        var principal = context.Principal;
        var claims = principal?.Claims.ToList() ?? [];
        string? Single(string type) => claims.Where(c => c.Type == type).Select(c => c.Value).SingleOrDefault();

        ClientApp? app = AppAccess.Parse(Single(AccessTokenClaims.App));
        if (!Guid.TryParse(Single(AccessTokenClaims.Subject), out var userId)
            || !Guid.TryParse(Single(AccessTokenClaims.Session), out var sessionId)
            || !int.TryParse(Single(AccessTokenClaims.SecurityVersion), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var securityVersion)
            || app is null)
        {
            context.Fail("The access token's sub, sid, app or sv claim is missing or malformed.");
            return;
        }

        var stamp = await context.HttpContext.RequestServices.GetRequiredService<ISecurityStampReader>()
            .GetAsync(userId, context.HttpContext.RequestAborted);
        if (stamp is null || stamp.SecurityVersion != securityVersion)
        {
            context.Fail("The access token is stale (security version changed).");
            return;
        }

        context.HttpContext.Features.Set(new AuthenticatedCaller(
            userId,
            sessionId,
            app.Value,
            securityVersion,
            claims.Where(c => c.Type == AccessTokenClaims.Permission).Select(c => c.Value).ToList(),
            claims.Where(c => c.Type == AccessTokenClaims.Role).Select(c => c.Value).ToList(),
            stamp.MustChangePassword));
    }
}
