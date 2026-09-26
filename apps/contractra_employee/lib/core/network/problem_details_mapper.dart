import 'package:dio/dio.dart';

import 'failures.dart';

/// Maps a Dio error carrying a ProblemDetails body (`{"code": "..."}`) to a
/// typed [Failure]. Codes come from the endpoint list in
/// claude-code-prompt-02-frontend.md; there is no published contract yet
/// (frontend-auth-and-users-plan.md, gap G1), so an unrecognized code is
/// treated as [UnknownFailure] rather than assumed to be a new case.
abstract final class ProblemDetailsMapper {
  static Failure fromDioException(DioException error) {
    if (error.type == DioExceptionType.connectionError ||
        error.type == DioExceptionType.connectionTimeout ||
        error.type == DioExceptionType.receiveTimeout ||
        error.type == DioExceptionType.sendTimeout) {
      return const NetworkFailure();
    }

    final statusCode = error.response?.statusCode;
    final code = _extractCode(error.response?.data);

    return switch (code) {
      'auth.invalid_credentials' => const InvalidCredentialsFailure(),
      'auth.password_change_required' => const PasswordChangeRequiredFailure(),
      'users.login_identifier_taken' => const LoginIdentifierTakenFailure(),
      'users.role_required' => const RoleRequiredFailure(),
      'users.disabled' => const UserDisabledFailure(),
      _ => switch (statusCode) {
          401 => const UnauthorizedFailure(),
          403 => const ForbiddenFailure(),
          _ => UnknownFailure(code: code),
        },
    };
  }

  static String? _extractCode(Object? responseData) {
    if (responseData is Map) {
      final value = responseData['code'];
      if (value is String) return value;
    }
    return null;
  }
}
