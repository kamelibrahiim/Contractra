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

/// UC-001's exception flow: never says which field is wrong.
final class LoginInvalidCredentials extends LoginState {
  const LoginInvalidCredentials();
}

final class LoginAccountDisabled extends LoginState {
  const LoginAccountDisabled();
}

final class LoginNetworkError extends LoginState {
  const LoginNetworkError();
}

/// On success the router's guard redirects away from /login once
/// SessionCubit reaches authenticated/passwordChangeRequired; this state
/// exists only so the UI can keep showing the loading button in that
/// brief window rather than snapping back to idle first.
final class LoginSucceeded extends LoginState {
  const LoginSucceeded();
}
