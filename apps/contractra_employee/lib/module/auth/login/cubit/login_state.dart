part of 'login_cubit.dart';

sealed class LoginState {
  const LoginState();

  @override
  bool operator ==(Object other) => other.runtimeType == runtimeType;

  @override
  int get hashCode => runtimeType.hashCode;
}

final class LoginIdle extends LoginState {
  const LoginIdle();
}

final class LoginSubmitting extends LoginState {
  const LoginSubmitting();
}

/// Local "both fields are required" failure only — never a server response.
final class LoginValidationError extends LoginState {
  const LoginValidationError();
}
