using System.Net;
using System.Security.Cryptography;
using System.Text;
using Contractra.Api.Tests.Support;
using Contractra.Application.Identity.Operations;
using Contractra.Infrastructure.IntegrationTests;

namespace Contractra.Api.Tests;

/// <summary>
/// Contract v1.5 §1, §3.1, §10A, §14, §18 (logical model §1.7, audit addendum §9.1,
/// NFR-SEC-002): passwords, temporary passwords, reset codes, access and refresh tokens
/// and their hashes are never logged, never audited, and never echoed in a response that
/// is not the token response itself.
/// </summary>
public sealed class SecretLeakTests : IClassFixture<PostgresTestDatabaseFixture>, IAsyncLifetime
{
    private readonly PostgresTestDatabaseFixture _database;
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public SecretLeakTests(PostgresTestDatabaseFixture database)
    {
        _database = database;
    }

    public Task InitializeAsync()
    {
        _factory = new ApiFactory(_database);
        _client = _factory.CreateApiClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task AFullAuthLifecycle_LeavesNoSecretInLogsAuditRowsOrErrorResponses()
    {
        var now = _factory.Clock.UtcNow;
        const string original = "Original-Passw0rd-XyZ";
        const string wrong = "Wrong-Guess-Passw0rd-QqQ";
        const string changed = "Changed-Passw0rd-AbC";
        const string chosen = "Chosen-After-Reset-PpP";
        var user = await Seed.UserAsync(_database, now, ["Engineer", "OfficeStaff"], password: original);
        var admin = await Seed.UserAsync(_database, now, ["Admin"], password: original);

        var nonTokenResponses = new List<ApiResponse>();
        var secrets = new List<string> { original, wrong, changed, chosen };

        // Failed and successful logins (mobile and web), an app-access denial.
        nonTokenResponses.Add(await Calls.LoginAsync(_client, user.Login, wrong));
        nonTokenResponses.Add(await Calls.LoginAsync(_client, Seed.NewLogin("ghost"), wrong));
        nonTokenResponses.Add(await Calls.LoginAsync(_client, user.Login, original, "admin-typo"));
        var mobile = await Calls.LoginAsync(_client, user.Login, original);
        var web = await Calls.LoginAsync(_client, admin.Login, original, "admin", "web");
        nonTokenResponses.Add(await Calls.LoginAsync(_client, Seed.NewLogin("x"), original, "admin"));
        var engineerOnly = await Seed.UserAsync(_database, now, ["Engineer"], password: original);
        nonTokenResponses.Add(await Calls.LoginAsync(_client, engineerOnly.Login, original, "admin"));
        Assert.Equal(HttpStatusCode.OK, mobile.Status);
        Assert.Equal(HttpStatusCode.OK, web.Status);
        secrets.Add(mobile.String("accessToken")!);
        secrets.Add(mobile.String("refreshToken")!);
        secrets.Add(web.String("accessToken")!);
        secrets.Add(Calls.RefreshCookieValue(web)!);

        // Refreshes, a CSRF rejection, reuse after the grace.
        var refreshed = await Calls.RefreshMobileAsync(_client, mobile.String("refreshToken")!);
        secrets.Add(refreshed.String("accessToken")!);
        secrets.Add(refreshed.String("refreshToken")!);
        nonTokenResponses.Add(await Calls.RefreshWebAsync(_client, Calls.RefreshCookieValue(web)!, csrfHeader: false));
        nonTokenResponses.Add(await Calls.RefreshMobileAsync(_client, "made-up-refresh-token-value"));

        // Change password: failures, then success.
        var access = refreshed.String("accessToken")!;
        nonTokenResponses.Add(await Calls.ChangePasswordAsync(_client, access, wrong, changed));
        nonTokenResponses.Add(await Calls.ChangePasswordAsync(_client, access, original, "short"));
        nonTokenResponses.Add(await Calls.ChangePasswordAsync(_client, access, original, original));
        var afterChange = await Calls.ChangePasswordAsync(_client, access, original, changed);
        Assert.Equal(HttpStatusCode.OK, afterChange.Status);
        secrets.Add(afterChange.String("accessToken")!);
        secrets.Add(afterChange.String("refreshToken")!);
        nonTokenResponses.Add(await Calls.MeAsync(_client, afterChange.String("accessToken")!));

        // Reset: issue through the operator command's use case, then wrong and right codes.
        string code;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            code = (await scope.ServiceProvider.GetRequiredService<OperatorIssuePasswordResetUseCase>()
                .ExecuteAsync(new OperatorIssuePasswordResetRequest(user.Login, "leak test", Guid.NewGuid().ToString("D")))).Value!.Code;
        }

        secrets.Add(code);
        secrets.Add(code.Replace("-", string.Empty));
        nonTokenResponses.Add(await Calls.CompleteResetAsync(_client, user.Login, "ZZZZ-ZZZZ-ZZZZ-ZZZZ", chosen));
        nonTokenResponses.Add(await Calls.CompleteResetAsync(_client, user.Login, code, "short"));
        nonTokenResponses.Add(await Calls.CompleteResetAsync(_client, user.Login, code, chosen));
        nonTokenResponses.Add(await Calls.LogoutAsync(_client, web.String("accessToken")!, csrfHeader: true));

        // Hashes: the stored password hashes and the refresh-token hashes.
        foreach (var id in new[] { user.Id, admin.Id })
        {
            secrets.Add((await Seed.UserByIdAsync(_database, id)).PasswordHash);
        }

        foreach (var token in secrets.ToList().Where(s => s.Length == 43))
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            secrets.Add(Convert.ToHexString(hash));
            secrets.Add(Convert.ToBase64String(hash));
        }

        var logs = _factory.Logs.AllText;
        var audit = await Seed.AuditTableTextAsync(_database);
        Assert.Contains("AccessDenied", audit, StringComparison.Ordinal);
        Assert.Contains("PasswordChanged", audit, StringComparison.Ordinal);
        Assert.Contains("PasswordResetCompleted", audit, StringComparison.Ordinal);

        foreach (var secret in secrets.Where(s => s.Length > 4).Distinct())
        {
            Assert.DoesNotContain(secret, logs, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(secret, audit, StringComparison.OrdinalIgnoreCase);
            foreach (var response in nonTokenResponses)
            {
                Assert.DoesNotContain(secret, response.AllText, StringComparison.Ordinal);
            }
        }

        // Audit rows never carry a login identifier either (ADR 0006 §5.2).
        Assert.DoesNotContain(user.Login, audit, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(admin.Login, audit, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(engineerOnly.Login, audit, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TheAuthorizationHeaderAndCookieAreNeverLogged()
    {
        var user = await Seed.UserAsync(_database, _factory.Clock.UtcNow, ["Admin"]);
        var web = await Calls.LoginAsync(_client, user.Login, user.Password, "admin", "web");
        var cookie = Calls.RefreshCookieValue(web)!;
        await Calls.RefreshWebAsync(_client, cookie);
        await Calls.MeAsync(_client, web.String("accessToken")!);
        await Calls.MeAsync(_client, "Bearer-garbage-token-value-123");

        var logs = _factory.Logs.AllText;
        Assert.DoesNotContain(cookie, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(web.String("accessToken")!, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(web.String("accessToken")!.Split('.')[1], logs, StringComparison.Ordinal);
        Assert.DoesNotContain("Bearer-garbage-token-value-123", logs, StringComparison.Ordinal);
        Assert.DoesNotContain(user.Password, logs, StringComparison.Ordinal);
    }
}
