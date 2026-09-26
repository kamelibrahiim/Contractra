/// Typed failures the UI can switch on. Never expose a raw ProblemDetails
/// payload or HTTP status to a screen — go through
/// [ProblemDetailsMapper] first.
sealed class Failure {
  const Failure();
}

final class InvalidCredentialsFailure extends Failure {
  const InvalidCredentialsFailure();
}

final class PasswordChangeRequiredFailure extends Failure {
  const PasswordChangeRequiredFailure();
}

final class LoginIdentifierTakenFailure extends Failure {
  const LoginIdentifierTakenFailure();
}

final class RoleRequiredFailure extends Failure {
  const RoleRequiredFailure();
}

final class UserDisabledFailure extends Failure {
  const UserDisabledFailure();
}

/// No network connection, or the server could not be reached at all.
final class NetworkFailure extends Failure {
  const NetworkFailure();
}

/// 401 that a refresh could not resolve; the caller should be signed out.
final class UnauthorizedFailure extends Failure {
  const UnauthorizedFailure();
}

/// 403 unrelated to [PasswordChangeRequiredFailure] — the session lacks a
/// required permission.
final class ForbiddenFailure extends Failure {
  const ForbiddenFailure();
}

/// Any ProblemDetails code this client does not recognize yet, or a
/// response with no ProblemDetails body at all. [code] is kept for
/// diagnostics only and must never be shown to the user.
final class UnknownFailure extends Failure {
  const UnknownFailure({this.code});

  final String? code;
}
