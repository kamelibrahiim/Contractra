namespace Contractra.Api.Auth;

/// <summary>Contract v1.5 §4.1: the app audience every endpoint must declare.</summary>
public enum AuthAudience
{
    /// <summary>No access token (login, refresh, password-reset completion).</summary>
    Public,

    /// <summary>A valid token of either app (logout, me, change-password).</summary>
    AnySession,

    /// <summary><c>app = admin</c> and <c>perm</c> contains <c>Apps.Admin.Access</c> (every /users endpoint, GET /roles - Slice 7).</summary>
    Admin,

    /// <summary><c>app = employee</c> and <c>perm</c> contains <c>Apps.Employee.Access</c> (future Employee-app endpoints).</summary>
    Employee,
}

/// <summary>An endpoint's declared audience. An endpoint without one is refused (fails closed, §4.1).</summary>
public sealed record AuthAudienceMetadata(AuthAudience Audience);

/// <summary>Marks the endpoints contract §7 allows while <c>mustChangePassword</c> is true.</summary>
public sealed class AllowedWhilePasswordChangeRequiredMetadata
{
    public static readonly AllowedWhilePasswordChangeRequiredMetadata Instance = new();
}

/// <summary>The permission an endpoint requires (contract §9; checked by Slice 7). Read here for AccessDenied's actor_role_code (§14.1).</summary>
public sealed record RequiredPermissionMetadata(string Permission);

/// <summary>Which in-memory rate-limit layers of contract §17 apply: 1 (all public auth endpoints) and 5 (refresh).</summary>
public sealed record RateLimitLayersMetadata(bool PublicLayer, bool RefreshLayer);

public static class AuthEndpointConventions
{
    public static TBuilder WithAudience<TBuilder>(this TBuilder builder, AuthAudience audience)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new AuthAudienceMetadata(audience));

    public static TBuilder AllowedWhilePasswordChangeRequired<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(AllowedWhilePasswordChangeRequiredMetadata.Instance);

    public static TBuilder WithRateLimitLayers<TBuilder>(this TBuilder builder, bool publicLayer, bool refreshLayer)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new RateLimitLayersMetadata(publicLayer, refreshLayer));
}
