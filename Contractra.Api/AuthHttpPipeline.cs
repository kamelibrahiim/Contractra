using System.Globalization;
using System.Security.Authentication;
using Contractra.Api.Auth;
using Contractra.Api.Http;
using Contractra.Application.Common;
using Contractra.Application.Identity.Auth;
using Contractra.Infrastructure;
using Contractra.Infrastructure.Persistence.Throttling;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Serilog;
using Serilog.Events;

namespace Contractra.Api;

/// <summary>
/// Slice 6 composition for the web host only (the operator-command branch of
/// Program.cs never reaches it): Serilog, the auth services, JWT validation with the
/// key ring, CORS, HSTS, forwarded headers, the throttle cleanup job, and the HTTP
/// pipeline in contract order. Invalid security configuration stops startup.
/// </summary>
public static class AuthHttpPipeline
{
    public const string CorsOriginsSection = "Cors:AllowedOrigins";

    /// <summary>Serilog with the console sink only; request bodies are never logged (contract §1, §18).</summary>
    public static WebApplicationBuilder AddContractraSerilog(this WebApplicationBuilder builder)
    {
        builder.Logging.ClearProviders();
        builder.Services.AddSerilog(
            (_, configuration) => configuration
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
                .Enrich.FromLogContext()
                .WriteTo.Console(
                    outputTemplate: "[{Timestamp:O} {Level:u3}] {SourceContext} {CorrelationId} {Message:lj}{NewLine}{Exception}",
                    formatProvider: CultureInfo.InvariantCulture),
            // Each host owns its logger; the process-wide static Log.Logger is never used.
            preserveStaticLogger: true,
            writeToProviders: true);
        return builder;
    }

    public static WebApplicationBuilder AddContractraAuthHttpPipeline(this WebApplicationBuilder builder)
    {
        var configuration = builder.Configuration;
        var services = builder.Services;

        var ring = JwtKeyRing.Load(configuration);
        var throttle = new AuthThrottleOptions();
        configuration.GetSection("Auth:Throttle").Bind(throttle);
        services.AddAuthInfrastructure(throttle, LoadSessionPolicy(configuration));

        services.AddSingleton(ring);
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<JwtKeyRing, IClock>((options, keyRing, clock) => JwtValidation.Configure(options, keyRing, clock));

        services.AddSingleton<AuthRateLimiter>();

        // Validate the trusted-proxy lists now, so a bad value stops startup.
        ClientSource.ConfigureForwardedHeaders(new ForwardedHeadersOptions(), configuration);
        services.Configure<ForwardedHeadersOptions>(options => ClientSource.ConfigureForwardedHeaders(options, configuration));

        var origins = LoadCorsOrigins(configuration);
        services.AddCors(options => options.AddDefaultPolicy(policy => policy
            .WithOrigins(origins)
            .AllowCredentials()
            .WithMethods("GET", "POST")
            .WithHeaders("Authorization", "Content-Type", AccessDenial.CsrfHeaderName, CorrelationIdMiddleware.HeaderName, AuthEndpoints.ClientPlatformHeader)
            .WithExposedHeaders(CorrelationIdMiddleware.HeaderName, "Retry-After")));

        services.AddHsts(options =>
        {
            options.MaxAge = TimeSpan.FromDays(365);
            options.IncludeSubDomains = false;
            options.Preload = false;
        });

        builder.WebHost.ConfigureKestrel(kestrel =>
            kestrel.ConfigureHttpsDefaults(https => https.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13));

        services.AddSingleton<AuthThrottleCleanupService>();
        services.AddHostedService(sp => sp.GetRequiredService<AuthThrottleCleanupService>());

        return builder;
    }

    public static WebApplication UseContractraAuthHttpPipeline(this WebApplication app)
    {
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseSerilogRequestLogging();
        app.UseExceptionHandler(errorApp => errorApp.Run(Problems.ServerErrorAsync));
        app.UseForwardedHeaders();
        if (!app.Environment.IsDevelopment())
        {
            app.UseMiddleware<HttpsOnlyMiddleware>();
            app.UseHsts();
        }

        app.UseRouting();
        app.UseCors();
        app.UseMiddleware<AuthRateLimitMiddleware>();
        app.UseAuthentication();
        app.UseMiddleware<AudienceGateMiddleware>();
        return app;
    }

    public static AuthSessionPolicy LoadSessionPolicy(IConfiguration configuration)
    {
        var idle = new Dictionary<string, TimeSpan>(AuthSessionPolicy.DefaultIdleTimeouts, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in configuration.GetSection("Auth:IdleTimeout").GetChildren())
        {
            if (!TimeSpan.TryParse(entry.Value, CultureInfo.InvariantCulture, out var timeout))
            {
                throw new InvalidOperationException($"Auth:IdleTimeout:{entry.Key} is not a time span (for example 08:00:00).");
            }

            idle[entry.Key] = timeout;
        }

        try
        {
            return new AuthSessionPolicy(idle);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidOperationException(exception.Message, exception);
        }
    }

    public static string[] LoadCorsOrigins(IConfiguration configuration)
    {
        var origins = configuration.GetSection(CorsOriginsSection).Get<string[]>() ?? [];
        foreach (var origin in origins)
        {
            if (origin.Contains('*', StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Cors:AllowedOrigins must list explicit origins; '*' is never used with credentials (contract §8, §18).");
            }

            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
                || uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query))
            {
                throw new InvalidOperationException("Cors:AllowedOrigins entries must be origins such as https://admin.example.com.");
            }
        }

        return origins;
    }
}
