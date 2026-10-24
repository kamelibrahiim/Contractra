using Contractra.Api;
using Contractra.Api.Auth;
using Contractra.Api.Users;
using Contractra.Infrastructure;
using Contractra.Infrastructure.Startup;

// Operator-only commands (contract §10A, §11): run on the server, no web host, no HTTP endpoint.
if (OperatorCommandRunner.IsOperatorCommand(args))
{
    var environmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
        ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
        ?? Environments.Production;
    var commandBuilder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [], EnvironmentName = environmentName });
    commandBuilder.Services.AddInfrastructure(commandBuilder.Configuration);
    commandBuilder.Services.AddStartupInitialization(failClosedOnUnsafeAuditPrivileges: commandBuilder.Environment.IsProduction());

    using var commandHost = commandBuilder.Build();
    return await commandHost.Services.GetRequiredService<OperatorCommandRunner>().RunAsync(args, Console.Out, Console.Error);
}

var builder = WebApplication.CreateBuilder(args);

builder.AddContractraSerilog();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddStartupInitialization(failClosedOnUnsafeAuditPrivileges: builder.Environment.IsProduction());
// Slice 6: auth endpoints and the HTTP security pipeline (contract §3-§8, §12, §13, §17, §18).
// Invalid security configuration (signing keys, idle timeouts, CORS, proxies, throttle) stops startup.
builder.AddContractraAuthHttpPipeline();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Startup work, strictly in order: the audit privilege self-check, then the bootstrap seeder.
// A failure here stops startup (contract §11, §14.2; repository ADR 0006 E42).
await app.Services.GetRequiredService<StartupInitializer>().RunAsync();

app.UseContractraAuthHttpPipeline();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().WithAudience(AuthAudience.Public);
}

app.MapAuthEndpoints();
app.MapUsersEndpoints();

app.Run();
return 0;

/// <summary>Exposed to the WebApplicationFactory tests.</summary>
public partial class Program;
