using Contractra.Application.Identity;
using Contractra.Application.Identity.Auth;
using Contractra.Infrastructure.Identity;
using Contractra.Infrastructure.Persistence.Audit;
using Contractra.Infrastructure.Persistence.Auth;
using Contractra.Infrastructure.Persistence.Identity;
using Contractra.Infrastructure.Persistence.Throttling;
using Microsoft.Extensions.DependencyInjection;

namespace Contractra.Infrastructure;

/// <summary>
/// Slice 6 (US-BE-012) services for the web host: the login/refresh/logout use
/// cases, the session-version lookup, the contract §17 throttle policy over the
/// shared store, and AccessDenied in its own scope. The operator-command host never
/// registers them. <see cref="DependencyInjection.AddInfrastructure"/> must run first.
/// </summary>
public static class AuthDependencyInjection
{
    public static IServiceCollection AddAuthInfrastructure(
        this IServiceCollection services, AuthThrottleOptions throttleOptions, AuthSessionPolicy sessionPolicy)
    {
        ArgumentNullException.ThrowIfNull(throttleOptions);
        ArgumentNullException.ThrowIfNull(sessionPolicy);
        throttleOptions.Validate();

        services.AddSingleton(throttleOptions);
        services.AddSingleton(sessionPolicy);

        services.AddScoped<UserAccessReader>();
        services.AddScoped<IUserAccessReader>(sp => sp.GetRequiredService<UserAccessReader>());
        services.AddScoped<ISecurityStampReader>(sp => sp.GetRequiredService<UserAccessReader>());
        services.AddScoped<IAuthSessionRepository, AuthSessionRepository>();
        services.AddScoped<IPasswordRehashStore, PasswordRehashStore>();
        services.AddSingleton<IRefreshTokenFactory, SecureRefreshTokenFactory>();
        services.AddSingleton<DummyPasswordHash>(sp => new DummyPasswordHash(sp.GetRequiredService<IPasswordHasher>()));

        services.AddScoped<AuthThrottleStore>();
        services.AddSingleton<IAuthThrottle, AuthThrottle>();
        services.AddSingleton<IAccessDeniedAuditor, ScopedAccessDeniedAuditor>();

        services.AddScoped<LoginUseCase>();
        services.AddScoped<RefreshUseCase>();
        services.AddScoped<LogoutUseCase>();
        services.AddScoped<CurrentUserQuery>();
        services.AddScoped<ThrottledChangePassword>();
        services.AddScoped<ThrottledPasswordResetCompletion>();

        return services;
    }
}
