using Contractra.Application.Audit;
using Contractra.Application.Common;
using Contractra.Application.Identity;
using Contractra.Application.Identity.Auth;
using Contractra.Application.Identity.Credentials;
using Contractra.Application.Identity.Usernames;
using Contractra.Application.Identity.Operations;
using Contractra.Application.Identity.UseCases;
using Contractra.Application.Identity.Users;
using Contractra.Infrastructure.Startup;
using Contractra.Application.Parties;
using Contractra.Infrastructure.Common;
using Contractra.Infrastructure.Identity;
using Contractra.Infrastructure.Persistence;
using Contractra.Infrastructure.Persistence.Audit;
using Contractra.Infrastructure.Persistence.Auth;
using Contractra.Infrastructure.Persistence.Identity;
using Contractra.Infrastructure.Persistence.Parties;
using Contractra.Infrastructure.Persistence.Throttling;
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
		services.AddScoped<IThrottleIdentifierEraser, ThrottleIdentifierEraser>();
		services.AddScoped<IUserRepository, UserRepository>();
		services.AddScoped<IRoleRepository, RoleRepository>();
		services.AddScoped<IPartyRepository, PartyRepository>();
		services.AddScoped<ISessionStore, SessionStore>();
		services.AddScoped<IPasswordResetTicketStore, PasswordResetTicketStore>();
		services.AddScoped<IUserDirectory, UserDirectory>();
		// Read by the user use cases, so the operator-command host (no AddAuthInfrastructure) needs it too.
		services.AddScoped<UserAccessReader>();
		services.AddScoped<IUserAccessReader>(sp => sp.GetRequiredService<UserAccessReader>());
		services.AddScoped<ISecurityStampReader>(sp => sp.GetRequiredService<UserAccessReader>());

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

		// PS-04a (contract v1.6 §23, ADR 0007): credential issuance, sealed copy and WhatsApp delivery.
		// The composition root (Api) replaces the sender; by default none is configured, so create and
		// reset answer 503 rather than queueing into nothing.
		services.AddSingleton(CredentialSealingKeyRing.Load(configuration));
		services.AddSingleton<AesGcmCredentialSealer>();
		services.AddSingleton<ICredentialSealing>(sp => sp.GetRequiredService<AesGcmCredentialSealer>());
		services.AddSingleton<ICredentialOpening>(sp => sp.GetRequiredService<AesGcmCredentialSealer>());
		services.AddSingleton(LoadDeliveryOptions(configuration));
		services.AddSingleton<IWhatsAppSender, UnconfiguredWhatsAppSender>();
		services.AddScoped<ICredentialIssuanceStore, CredentialIssuanceStore>();
		services.AddSingleton<UsernamePolicy>();
		services.AddSingleton<UsernameSuggester>();
		services.AddScoped<ReissueCredentialUseCase>();
		services.AddScoped<DeliverCredentialUseCase>();
		services.AddScoped<SweepCredentialsUseCase>();
		services.AddScoped<ApplyWhatsAppStatusUseCase>();

		// Slice 7: read side of the Admin users endpoints (contract §9.2, §9.3, §9.7).
		services.AddScoped<GetUserQuery>();
		services.AddScoped<ListUsersQuery>();
		services.AddScoped<ListRolesQuery>();

		// Slice 5: bootstrap seeder and the two operator commands (contract §10A, §11).
		services.AddScoped<IPermissionRepository, PermissionRepository>();
		services.AddScoped<SeedBootstrapAdminUseCase>();
		services.AddScoped<OperatorIssuePasswordResetUseCase>();
		services.AddScoped<ResetBootstrapPasswordUseCase>();
		services.AddScoped<ClearThrottleUseCase>();

		return services;
	}

	private static CredentialDeliveryOptions LoadDeliveryOptions(IConfiguration configuration)
	{
		var section = configuration.GetSection(CredentialDeliveryOptions.Section);
		var options = new CredentialDeliveryOptions();

		if (bool.TryParse(section["Enabled"], out var enabled))
		{
			options.Enabled = enabled;
		}

		if (TimeSpan.TryParse(section["SealedCopyRetention"], System.Globalization.CultureInfo.InvariantCulture, out var retention) && retention > TimeSpan.Zero)
		{
			options.SealedCopyRetention = retention;
		}

		if (int.TryParse(section["MaxAttempts"], out var attempts) && attempts > 0)
		{
			options.MaxAttempts = attempts;
		}

		if (TimeSpan.TryParse(section["BackoffBase"], System.Globalization.CultureInfo.InvariantCulture, out var backoff) && backoff > TimeSpan.Zero)
		{
			options.BackoffBase = backoff;
		}

		if (TimeSpan.TryParse(section["MinReissueInterval"], System.Globalization.CultureInfo.InvariantCulture, out var interval) && interval >= TimeSpan.Zero)
		{
			options.MinReissueInterval = interval;
		}

		if (int.TryParse(section["MaxReissuesPer24Hours"], out var cap) && cap > 0)
		{
			options.MaxReissuesPer24Hours = cap;
		}

		if (TimeSpan.TryParse(section["StalledAfter"], System.Globalization.CultureInfo.InvariantCulture, out var stalled) && stalled > TimeSpan.Zero)
		{
			options.StalledAfter = stalled;
		}

		if (TimeSpan.TryParse(section["WorkerPollInterval"], System.Globalization.CultureInfo.InvariantCulture, out var poll) && poll > TimeSpan.Zero)
		{
			options.WorkerPollInterval = poll;
		}

		return options;
	}

	/// <summary>
	/// Startup work and operator commands. The audit privilege self-check is
	/// registered before - and ordered ahead of - the bootstrap seeder, so the
	/// seeder never writes with an unsafe database role (repository ADR 0006 E42).
	/// </summary>
	/// <param name="failClosedOnUnsafeAuditPrivileges">True in Production: an unsafe role stops startup; elsewhere it is a warning.</param>
	public static IServiceCollection AddStartupInitialization(
		this IServiceCollection services, bool failClosedOnUnsafeAuditPrivileges)
	{
		services.AddSingleton(new StartupOptions(failClosedOnUnsafeAuditPrivileges));
		services.AddSingleton<IStartupStep, AuditPrivilegeStartupStep>();
		services.AddSingleton<IStartupStep, BootstrapSeederStartupStep>();
		services.AddSingleton<StartupInitializer>();
		services.AddSingleton<OperatorCommandRunner>();
		return services;
	}
}