using Contractra.Application.Audit;
using Contractra.Application.Common;
using Contractra.Application.Identity;
using Contractra.Application.Identity.UseCases;
using Contractra.Application.Parties;
using Contractra.Infrastructure.Common;
using Contractra.Infrastructure.Identity;
using Contractra.Infrastructure.Persistence;
using Contractra.Infrastructure.Persistence.Audit;
using Contractra.Infrastructure.Persistence.Auth;
using Contractra.Infrastructure.Persistence.Identity;
using Contractra.Infrastructure.Persistence.Parties;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace Contractra.Infrastructure;

public static class DependencyInjection
{
	public static IServiceCollection AddInfrastructure(
		this IServiceCollection services,
		IConfiguration configuration)
	{
		var connectionString =
			configuration.GetConnectionString("DefaultConnection")
			?? throw new InvalidOperationException(
				"Connection string 'DefaultConnection' was not found.");

		services.AddDbContext<ContractraDbContext>(options =>
			options.UseNpgsql(connectionString));

		// Scoped: it stages into the same ContractraDbContext as the use case.
		services.AddScoped<IAuditWriter, AuditWriter>();

		// Use cases log through ILogger (Serilog plugs in at the API composition root).
		services.AddLogging();

		services.AddScoped<IUnitOfWork, UnitOfWork>();
		services.AddScoped<IUserRepository, UserRepository>();
		services.AddScoped<IRoleRepository, RoleRepository>();
		services.AddScoped<IPartyRepository, PartyRepository>();
		services.AddScoped<ISessionStore, SessionStore>();
		services.AddScoped<IPasswordResetTicketStore, PasswordResetTicketStore>();

		services.AddSingleton<IClock, SystemClock>();
		services.AddSingleton<IBusinessDateProvider, ConfiguredBusinessDateProvider>();
		services.AddSingleton<IPasswordHasher, AspNetPasswordHasher>();
		services.AddSingleton<ITemporaryPasswordGenerator, SecureTemporaryPasswordGenerator>();
		services.AddSingleton<IResetCodeGenerator, CrockfordResetCodeGenerator>();

		services.AddScoped<CreateUserUseCase>();
		services.AddScoped<ChangePasswordUseCase>();
		services.AddScoped<DisableUserUseCase>();
		services.AddScoped<ReactivateUserUseCase>();
		services.AddScoped<IssuePasswordResetUseCase>();
		services.AddScoped<CompletePasswordResetUseCase>();
		services.AddScoped<ReissueInitialPasswordUseCase>();

		return services;
	}
}