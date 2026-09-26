import '../../../../core/network/failures.dart';
import '../models/current_user.dart';
import '../models/login_result.dart';

/// Result type used instead of exceptions so cubits can pattern-match
/// without a try/catch per call site.
sealed class AuthResult<T> {
  const AuthResult();
}

final class AuthSuccess<T> extends AuthResult<T> {
  const AuthSuccess(this.value);
  final T value;
}

final class AuthError<T> extends AuthResult<T> {
  const AuthError(this.failure);
  final Failure failure;
}

abstract class AuthRepository {
  Future<AuthResult<LoginResult>> login(String identifier, String password);

  /// Returns the new access token, or throws if the refresh token is
  /// invalid/expired — callers (the interceptor, session restore) treat a
  /// throw as "the session is over".
  Future<String> refresh();

  Future<void> logout();

  Future<AuthResult<CurrentUser>> me();

  Future<AuthResult<void>> changePassword({
    required String currentPassword,
    required String newPassword,
  });
}
