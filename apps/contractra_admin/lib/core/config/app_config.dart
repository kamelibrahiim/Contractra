/// Runtime configuration from `--dart-define`. No values are hardcoded for
/// a specific environment; dev/prod pass different defines at build time.
///
/// Mock mode is the default until the backend auth/users API exists
/// (see docs/implementation/sprints/frontend-auth-and-users-plan.md).
abstract final class AppConfig {
  static const String apiBaseUrl = String.fromEnvironment(
    'API_BASE_URL',
    defaultValue: 'http://localhost:5000',
  );

  static const bool useMockApi = bool.fromEnvironment(
    'USE_MOCK_API',
    defaultValue: true,
  );
}
