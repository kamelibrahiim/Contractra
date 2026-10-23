using System.Globalization;
using Contractra.Api.Auth;
using Contractra.Api.Http;
using Contractra.Application.Common;
using Contractra.Application.Identity;
using Contractra.Application.Identity.Auth;
using Contractra.Application.Identity.Credentials;
using Contractra.Application.Identity.UseCases;
using Contractra.Application.Identity.Usernames;
using Contractra.Application.Identity.Users;
using Contractra.Domain.Identity;
using Npgsql;

namespace Contractra.Api.Users;

/// <summary>
/// <c>/api/v1/users</c> and <c>/api/v1/roles</c> (auth contract v1.5 §9 as amended by v1.6 §23). Every
/// endpoint declares audience <c>admin</c> and requires <c>Users.Manage</c>; steps 1-4 of the §9
/// check order (authentication, password-change gate, app audience, permission) are
/// enforced by <see cref="AudienceGateMiddleware"/> before any handler runs. Handlers only
/// translate HTTP to the use cases and queries and back; the target lookup, bootstrap
/// protection and state-transition checks (steps 5-7) live in the use cases. No response
/// ever carries a password (R51).
/// </summary>
public static class UsersEndpoints
{
    public const string ManagePermission = "Users.Manage";

    public static IEndpointRouteBuilder MapUsersEndpoints(this IEndpointRouteBuilder app)
    {
        var users = app.MapGroup("/api/v1/users")
            .WithAudience(AuthAudience.Admin)
            .RequiresPermission(ManagePermission);

        users.MapPost("", CreateAsync);
        users.MapGet("", ListAsync);
        users.MapPost("/username-suggestions", UsernameSuggestionsAsync);
        users.MapGet("/{id}", GetAsync);
        users.MapPost("/{id}/disable", DisableAsync);
        users.MapPost("/{id}/reactivate", ReactivateAsync);
        users.MapPost("/{id}/reset-password", ResetPasswordAsync);
        users.MapGet("/{id}/credential-delivery", CredentialDeliveryAsync);
        // v1.6 (contract §23.1): POST /users/{id}/initial-password is retired - it returned a password to the Admin.

        app.MapGroup("/api/v1/roles")
            .WithAudience(AuthAudience.Admin)
            .RequiresPermission(ManagePermission)
            .MapGet("", ListRolesAsync);

        return app;
    }

    // POST /api/v1/users - §9.1 as replaced by §23.2 (no password in the request or the response)
    private static async Task CreateAsync(HttpContext context)
    {
        var caller = context.RequireCaller();
        var body = await AuthRequestBody.ReadAsync(context.Request);
        if (body.Status is BodyStatus.TooLarge or BodyStatus.Malformed)
        {
            await MalformedAsync(context);
            return;
        }

        // A field of the wrong type (or a partyId that is not a GUID) is a request-shape
        // error: 400 validation.failed. An absent field is passed on as "nothing supplied"
        // so the use case reports the documented code.
        var loginIdentifier = body.GetString("loginIdentifier");
        var roleCodes = body.GetStringArray("roleCodes");
        var partyIdText = body.GetString("partyId");
        var whatsAppNumber = body.GetStringAt("whatsapp", "number");
        var consentAffirmed = body.GetBooleanAt("whatsapp", "consent", "affirmed");
        var consentBasis = body.GetStringAt("whatsapp", "consent", "basis");
        Guid? partyId = null;
        if (partyIdText is not null)
        {
            if (Guid.TryParse(partyIdText, out var parsed))
            {
                partyId = parsed;
            }
            else
            {
                body.AddFieldError("partyId", "The value must be a GUID.");
            }
        }

        if (body.TypeErrors.Count > 0)
        {
            await Problems.WriteCodeAsync(context, AuthErrorCodes.ValidationFailed, body.TypeErrorMessages());
            return;
        }

        var request = new CreateUserRequest(
            loginIdentifier ?? string.Empty,
            roleCodes ?? [],
            partyId,
            whatsAppNumber,
            consentAffirmed ?? false,
            consentBasis,
            caller.UserId,
            CorrelationIdMiddleware.Get(context),
            AppAccess.AuditSourceFor(caller.App));

        UseCaseResult<CreateUserResult> result;
        try
        {
            result = await context.RequestServices.GetRequiredService<CreateUserUseCase>().ExecuteAsync(request, context.RequestAborted);
        }
        catch (Exception exception) when (UniqueViolationCode(exception) is { } code)
        {
            // §9.1: a concurrent create of the same identifier or Party is caught by the unique indexes.
            result = UseCaseResult<CreateUserResult>.Failure(code, "Lost a concurrent create.");
        }

        if (!result.IsSuccess)
        {
            await WriteFailureAsync(context, caller, result.Error!, request.LoginIdentifier);
            return;
        }

        var created = result.Value!;
        var user = await context.RequestServices.GetRequiredService<GetUserQuery>().ExecuteAsync(created.UserId, context.RequestAborted);
        if (user is null)
        {
            await Problems.ServerErrorAsync(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status201Created;
        context.Response.Headers.Location = $"/api/v1/users/{created.UserId:D}";
        context.Response.Headers.CacheControl = "no-store";
        await context.Response.WriteAsJsonAsync(new Dictionary<string, object?>
        {
            ["user"] = ToJson(user),
            ["credentialDelivery"] = DeliveryJson(created.Delivery),
        });
    }

    // POST /api/v1/users/username-suggestions - §23.5
    private static async Task UsernameSuggestionsAsync(HttpContext context)
    {
        var body = await AuthRequestBody.ReadAsync(context.Request);
        if (body.Status is BodyStatus.TooLarge or BodyStatus.Malformed or BodyStatus.Empty)
        {
            await MalformedAsync(context);
            return;
        }

        var name = body.GetString("name");
        var latinName = body.GetString("latinName");
        var candidate = body.GetString("candidate");
        if (body.TypeErrors.Count > 0)
        {
            await Problems.WriteCodeAsync(context, AuthErrorCodes.ValidationFailed, body.TypeErrorMessages());
            return;
        }

        var services = context.RequestServices;
        var users = services.GetRequiredService<IUserRepository>();
        var roles = await services.GetRequiredService<IRoleRepository>().ListActiveCodesAsync(context.RequestAborted);
        var bootstrap = await users.FindBootstrapAdminAsync(context.RequestAborted);
        var policy = new UsernamePolicy(roles.Append(bootstrap?.LoginIdentifier.Value ?? string.Empty));
        var suggester = new UsernameSuggester(policy);

        Task<bool> IsTaken(string value) => users.ExistsWithLoginIdentifierAsync(value, context.RequestAborted);

        context.Response.Headers.CacheControl = "no-store";

        if (candidate is not null)
        {
            var validation = policy.Validate(candidate);
            if (!validation.IsValid)
            {
                await context.Response.WriteAsJsonAsync(new Dictionary<string, object?>
                {
                    ["policyVersion"] = UsernamePolicy.Version,
                    ["suggestions"] = Array.Empty<object>(),
                    ["reason"] = UsernameValidation.ReasonCode(validation.Reason!.Value),
                });
                return;
            }

            var taken = await IsTaken(validation.Canonical!);
            await context.Response.WriteAsJsonAsync(new Dictionary<string, object?>
            {
                ["policyVersion"] = UsernamePolicy.Version,
                ["suggestions"] = new[]
                {
                    new Dictionary<string, object?> { ["value"] = validation.Canonical, ["available"] = !taken, ["source"] = "candidate" },
                },
                ["alternatives"] = taken ? await suggester.AlternativesAsync(validation.Canonical!, IsTaken) : [],
            });
            return;
        }

        var suggestions = await suggester.SuggestAsync(name, latinName, IsTaken);
        if (suggestions.Reason is not null)
        {
            await context.Response.WriteAsJsonAsync(new Dictionary<string, object?>
            {
                ["policyVersion"] = suggestions.PolicyVersion,
                ["suggestions"] = Array.Empty<object>(),
                ["reason"] = suggestions.Reason,
            });
            return;
        }

        await context.Response.WriteAsJsonAsync(new Dictionary<string, object?>
        {
            ["policyVersion"] = suggestions.PolicyVersion,
            ["suggestions"] = suggestions.Suggestions
                .Select(x => new Dictionary<string, object?> { ["value"] = x.Value, ["available"] = x.Available, ["source"] = x.Source })
                .ToList(),
            ["alternatives"] = suggestions.Alternatives,
        });
    }

    // GET /api/v1/users - §9.2
    private static async Task ListAsync(HttpContext context)
    {
        var query = context.Request.Query;
        var errors = new Dictionary<string, string[]>();

        var page = ReadInt(query, "page", 1, errors, min: 1, max: int.MaxValue);
        var pageSize = ReadInt(query, "pageSize", UserListCriteria.DefaultPageSize, errors, min: 1, max: UserListCriteria.MaxPageSize);

        string? search = null;
        if (query.TryGetValue("search", out var searchValues))
        {
            if (searchValues.Count != 1)
            {
                errors["search"] = ["The parameter may be supplied only once."];
            }
            else if (searchValues[0] is { Length: > 0 } text)
            {
                if (text.Length > UserListCriteria.MaxSearchLength)
                {
                    errors["search"] = [$"The value must be at most {UserListCriteria.MaxSearchLength} characters."];
                }
                else if (text.Contains('\0'))
                {
                    // PostgreSQL text cannot hold NUL; it would surface as a 500.
                    errors["search"] = ["The value contains an invalid character."];
                }
                else
                {
                    search = text;
                }
            }
        }

        UserStatus? status = null;
        if (query.TryGetValue("status", out var statusValues) && !(statusValues.Count == 1 && statusValues[0] is null or ""))
        {
            status = statusValues.Count == 1 ? ParseStatus(statusValues[0]) : null;
            if (status is null)
            {
                errors["status"] = ["The value must be Active or Disabled."];
            }
        }

        if (errors.Count > 0)
        {
            await Problems.WriteCodeAsync(context, AuthErrorCodes.ValidationFailed, errors);
            return;
        }

        var result = await context.RequestServices.GetRequiredService<ListUsersQuery>()
            .ExecuteAsync(new UserListCriteria(page, pageSize, search, status), context.RequestAborted);

        context.Response.Headers.CacheControl = "no-store";
        await context.Response.WriteAsJsonAsync(new Dictionary<string, object?>
        {
            ["items"] = result.Items.Select(ToJson).ToList(),
            ["page"] = result.Page,
            ["pageSize"] = result.PageSize,
            ["totalCount"] = result.TotalCount,
        });
    }

    // GET /api/v1/users/{id} - §9.3
    private static async Task GetAsync(HttpContext context, string id)
    {
        if (!TryParseId(id, out var userId))
        {
            await Problems.WriteCodeAsync(context, UserErrorCodes.NotFound);
            return;
        }

        await WriteUserOrNotFoundAsync(context, userId);
    }

    // POST /api/v1/users/{id}/disable - §9.4
    private static async Task DisableAsync(HttpContext context, string id)
    {
        if (!TryParseId(id, out var userId))
        {
            await Problems.WriteCodeAsync(context, UserErrorCodes.NotFound);
            return;
        }

        var caller = context.RequireCaller();
        var request = new DisableUserRequest(userId, caller.UserId, CorrelationIdMiddleware.Get(context), AppAccess.AuditSourceFor(caller.App));
        var result = await AuthEndpoints.WithConcurrencyRetryAsync(context, services =>
            services.GetRequiredService<DisableUserUseCase>().ExecuteAsync(request, context.RequestAborted));
        if (!result.IsSuccess)
        {
            await WriteFailureAsync(context, caller, result.Error!);
            return;
        }

        await WriteUserOrNotFoundAsync(context, userId);
    }

    // POST /api/v1/users/{id}/reactivate - §9.5
    private static async Task ReactivateAsync(HttpContext context, string id)
    {
        if (!TryParseId(id, out var userId))
        {
            await Problems.WriteCodeAsync(context, UserErrorCodes.NotFound);
            return;
        }

        var caller = context.RequireCaller();
        var request = new ReactivateUserRequest(userId, caller.UserId, CorrelationIdMiddleware.Get(context), AppAccess.AuditSourceFor(caller.App));
        var result = await AuthEndpoints.WithConcurrencyRetryAsync(context, services =>
            services.GetRequiredService<ReactivateUserUseCase>().ExecuteAsync(request, context.RequestAborted));
        if (!result.IsSuccess)
        {
            await WriteFailureAsync(context, caller, result.Error!);
            return;
        }

        await WriteUserOrNotFoundAsync(context, userId);
    }

    // POST /api/v1/users/{id}/reset-password - §9.6 as replaced by §23.3
    private static async Task ResetPasswordAsync(HttpContext context, string id)
    {
        if (!TryParseId(id, out var userId))
        {
            await Problems.WriteCodeAsync(context, UserErrorCodes.NotFound);
            return;
        }

        // The optional body carries expectedIssuanceId only; the request can never name a destination.
        Guid? expected = null;
        var body = await AuthRequestBody.ReadAsync(context.Request);
        if (body.Status is BodyStatus.TooLarge or BodyStatus.Malformed)
        {
            await MalformedAsync(context);
            return;
        }

        var text = body.GetString("expectedIssuanceId");
        if (text is not null)
        {
            if (Guid.TryParse(text, out var parsed))
            {
                expected = parsed;
            }
            else
            {
                body.AddFieldError("expectedIssuanceId", "The value must be a GUID.");
            }
        }

        if (body.TypeErrors.Count > 0)
        {
            await Problems.WriteCodeAsync(context, AuthErrorCodes.ValidationFailed, body.TypeErrorMessages());
            return;
        }

        var caller = context.RequireCaller();
        var request = new ReissueCredentialRequest(userId, expected, caller.UserId, CorrelationIdMiddleware.Get(context), AppAccess.AuditSourceFor(caller.App));
        UseCaseResult<CredentialDeliveryView> result;
        try
        {
            result = await AuthEndpoints.WithConcurrencyRetryAsync(context, services =>
                services.GetRequiredService<ReissueCredentialUseCase>().ExecuteAsync(request, context.RequestAborted));
        }
        catch (Exception exception) when (exception.GetBaseException() is PostgresException { SqlState: PostgresErrorCodes.ExclusionViolation })
        {
            // Two concurrent re-issues of one account: the deferred exclusion constraint let only one commit.
            result = UseCaseResult<CredentialDeliveryView>.Failure(UserErrorCodes.IssuanceChanged, "Lost a concurrent re-issue.");
        }

        if (!result.IsSuccess)
        {
            await WriteFailureAsync(context, caller, result.Error!);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status202Accepted;
        context.Response.Headers.CacheControl = "no-store";
        await context.Response.WriteAsJsonAsync(new Dictionary<string, object?> { ["credentialDelivery"] = DeliveryJson(result.Value!) });
    }

    // GET /api/v1/users/{id}/credential-delivery - §23.4
    private static async Task CredentialDeliveryAsync(HttpContext context, string id)
    {
        if (!TryParseId(id, out var userId))
        {
            await Problems.WriteCodeAsync(context, UserErrorCodes.NotFound);
            return;
        }

        var services = context.RequestServices;
        if (await services.GetRequiredService<GetUserQuery>().ExecuteAsync(userId, context.RequestAborted) is null)
        {
            await Problems.WriteCodeAsync(context, UserErrorCodes.NotFound);
            return;
        }

        var options = services.GetRequiredService<CredentialDeliveryOptions>();
        var history = await services.GetRequiredService<ICredentialIssuanceStore>().ListSummariesForUserAsync(userId, 20, context.RequestAborted);
        if (history.Count == 0)
        {
            await Problems.WriteCodeAsync(context, UserErrorCodes.CredentialDeliveryNotFound);
            return;
        }

        var now = services.GetRequiredService<IClock>().UtcNow;
        context.Response.Headers.CacheControl = "no-store";
        await context.Response.WriteAsJsonAsync(new Dictionary<string, object?>
        {
            ["credentialDelivery"] = DeliveryJson(history[0].ToView(now, options.StalledAfter)),
            ["history"] = history.Skip(1).Select(h => new Dictionary<string, object?>
            {
                ["issuanceId"] = h.Id,
                ["kind"] = h.Kind.ToString(),
                ["status"] = h.Status.ToString(),
                ["attempts"] = h.Attempts,
                ["issuedAt"] = Utc(h.IssuedAt),
                ["updatedAt"] = Utc(h.UpdatedAt),
                ["failureCategory"] = CredentialDeliveryView.CategoryCode(h.FailureCategory),
            }).ToList(),
        });
    }

    // GET /api/v1/roles - §9.7
    private static async Task ListRolesAsync(HttpContext context)
    {
        var codes = await context.RequestServices.GetRequiredService<ListRolesQuery>().ExecuteAsync(context.RequestAborted);
        context.Response.Headers.CacheControl = "no-store";
        await context.Response.WriteAsJsonAsync(codes.Select(code => new Dictionary<string, object?> { ["code"] = code }).ToList());
    }

    private static async Task WriteUserOrNotFoundAsync(HttpContext context, Guid userId)
    {
        var user = await context.RequestServices.GetRequiredService<GetUserQuery>().ExecuteAsync(userId, context.RequestAborted);
        if (user is null)
        {
            await Problems.WriteCodeAsync(context, UserErrorCodes.NotFound);
            return;
        }

        context.Response.Headers.CacheControl = "no-store";
        await context.Response.WriteAsJsonAsync(ToJson(user));
    }

    private static Dictionary<string, object?> ToJson(UserSummary user) => new()
    {
        ["id"] = user.Id,
        ["loginIdentifier"] = user.LoginIdentifier,
        ["status"] = user.Status.ToString(),
        ["roles"] = user.Roles,
        ["isBootstrapAdmin"] = user.IsBootstrapAdmin,
        ["credentialState"] = user.CredentialState.ToString(),
    };

    private static Dictionary<string, object?> DeliveryJson(CredentialDeliveryView view) => new()
    {
        ["issuanceId"] = view.IssuanceId,
        ["kind"] = view.Kind.ToString(),
        ["status"] = view.Status.ToString(),
        ["attempts"] = view.Attempts,
        ["updatedAt"] = view.UpdatedAtUtc,
        ["destinationMasked"] = view.DestinationMasked,
        ["failureCategory"] = CredentialDeliveryView.CategoryCode(view.FailureCategory),
        ["stalled"] = view.Stalled,
    };

    /// <summary>
    /// Maps a use case failure to its ProblemDetails. Business failures (404/409/503, 400)
    /// are not <c>AccessDenied</c> events; a bootstrap-account rejection and an unavailable
    /// delivery channel are logged to Serilog with the correlation id (§9.6, §12).
    /// </summary>
    private static async Task WriteFailureAsync(
        HttpContext context, AuthenticatedCaller caller, UseCaseError error, string? attemptedLoginIdentifier = null)
    {
        switch (error.Code)
        {
            case UserErrorCodes.BootstrapAccountProtected:
                Logger(context).LogWarning(
                    "Users.BootstrapAccountProtected UserId={UserId} Action={Action} CorrelationId={CorrelationId}",
                    caller.UserId, $"{context.Request.Method} {context.Request.Path}", CorrelationIdMiddleware.Get(context));
                break;
            case UserErrorCodes.CredentialDeliveryUnavailable:
                Logger(context).LogWarning(
                    "Users.CredentialDeliveryUnavailable UserId={UserId} CorrelationId={CorrelationId}: delivery or the sealing key is unavailable; nothing changed.",
                    caller.UserId, CorrelationIdMiddleware.Get(context));
                break;
            case UserErrorCodes.ResetDeliveryUnavailable:
                Logger(context).LogWarning(
                    "Users.ResetDeliveryUnavailable UserId={UserId} CorrelationId={CorrelationId}: no delivery channel can reach the user; nothing changed.",
                    caller.UserId, CorrelationIdMiddleware.Get(context));
                break;
        }

        switch (error.Code)
        {
            case UserErrorCodes.PartyInvalid:
                // CreateUserUseCase puts the contract's reason in the message.
                var reason = error.Message is "not_found" or "not_active" or "no_active_engineer_role" ? error.Message : null;
                await Problems.WriteCodeAsync(context, error.Code, reason: reason);
                break;
            case UserErrorCodes.LoginIdentifierInvalid:
                // v1.6: the username-policy reason (too_short, reserved, ...).
                await Problems.WriteCodeAsync(context, error.Code, reason: error.Message);
                break;
            case UserErrorCodes.LoginIdentifierTaken when attemptedLoginIdentifier is not null:
                await WriteTakenAsync(context, error.Code, attemptedLoginIdentifier);
                break;
            case UserErrorCodes.ReissueTooSoon:
                // The use case puts the wait, in whole seconds, in the message.
                var wait = int.TryParse(error.Message, out var seconds) ? TimeSpan.FromSeconds(seconds) : TimeSpan.FromMinutes(1);
                await Problems.WriteCodeAsync(context, error.Code, retryAfter: wait);
                break;
            case AuthErrorCodes.ValidationFailed:
                var field = error.Message.Contains("partyId", StringComparison.Ordinal) ? "partyId" : "roleCodes";
                await Problems.WriteCodeAsync(context, error.Code, new Dictionary<string, string[]> { [field] = [error.Message] });
                break;
            default:
                await Problems.WriteCodeAsync(context, error.Code);
                break;
        }
    }

    /// <summary>409 users.login_identifier_taken with up to three free alternatives (contract §23.2).</summary>
    private static async Task WriteTakenAsync(HttpContext context, string code, string attempted)
    {
        var services = context.RequestServices;
        var users = services.GetRequiredService<IUserRepository>();
        var validation = services.GetRequiredService<UsernamePolicy>().Validate(attempted);
        IReadOnlyList<string> alternatives = validation.IsValid
            ? await services.GetRequiredService<UsernameSuggester>()
                .AlternativesAsync(validation.Canonical!, value => users.ExistsWithLoginIdentifierAsync(value, context.RequestAborted))
            : [];

        await Problems.WriteCodeAsync(context, code, alternatives: alternatives);
    }

    /// <summary>The users.* code for a unique-index violation of a concurrent create, or null for any other exception.</summary>
    private static string? UniqueViolationCode(Exception exception) =>
        exception.GetBaseException() is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } violation
            ? violation.ConstraintName switch
            {
                "ux_users_login_identifier_lower" => UserErrorCodes.LoginIdentifierTaken,
                "ux_user_party_links_party_id" => UserErrorCodes.PartyAlreadyLinked,
                _ => null,
            }
            : null;

    /// <summary>A route id that is not a GUID names no user: 404 users.not_found (after steps 1-4).</summary>
    private static bool TryParseId(string id, out Guid userId) => Guid.TryParse(id, out userId);

    private static UserStatus? ParseStatus(string? text) => text switch
    {
        _ when string.Equals(text, nameof(UserStatus.Active), StringComparison.OrdinalIgnoreCase) => UserStatus.Active,
        _ when string.Equals(text, nameof(UserStatus.Disabled), StringComparison.OrdinalIgnoreCase) => UserStatus.Disabled,
        _ => null,
    };

    private static int ReadInt(IQueryCollection query, string name, int defaultValue, Dictionary<string, string[]> errors, int min, int max)
    {
        if (!query.TryGetValue(name, out var values) || (values.Count == 1 && string.IsNullOrEmpty(values[0])))
        {
            return defaultValue;
        }

        if (values.Count != 1
            || !int.TryParse(values[0], NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            || value < min
            || value > max)
        {
            errors[name] = [max == int.MaxValue ? $"The value must be an integer of at least {min}." : $"The value must be an integer from {min} to {max}."];
            return defaultValue;
        }

        return value;
    }

    private static string Utc(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static Task MalformedAsync(HttpContext context) =>
        Problems.WriteCodeAsync(context, AuthErrorCodes.ValidationFailed, new Dictionary<string, string[]>
        {
            ["body"] = [$"The body must be a JSON object of at most {AuthRequestBody.MaxBytes} bytes."],
        });

    private static ILogger Logger(HttpContext context) =>
        context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Contractra.Api.Users.UsersEndpoints");
}
