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

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddStartupInitialization(failClosedOnUnsafeAuditPrivileges: builder.Environment.IsProduction());
// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Startup work, strictly in order: the audit privilege self-check, then the bootstrap seeder.
// A failure here stops startup (contract §11, §14.2; repository ADR 0006 E42).
await app.Services.GetRequiredService<StartupInitializer>().RunAsync();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast =  Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");

app.Run();
return 0;

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
