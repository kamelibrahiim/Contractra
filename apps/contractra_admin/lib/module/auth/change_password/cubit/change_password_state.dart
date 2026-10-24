part of 'change_password_cubit.dart';

sealed class ChangePasswordState {
  const ChangePasswordState();

  @override
  bool operator ==(Object other) => other.runtimeType == runtimeType;

  @override
  int get hashCode => runtimeType.hashCode;
}

final class ChangePasswordIdle extends ChangePasswordState {
  const ChangePasswordIdle();
}

final class ChangePasswordSubmitting extends ChangePasswordState {
  const ChangePasswordSubmitting();
}

final class ChangePasswordSuccess extends ChangePasswordState {
  const ChangePasswordSuccess();
}

/// Local rule failures (min length, default password, mismatch) — distinct
/// from [ChangePasswordServerError] so the UI can tell "fix your input"
/// apart from "the server rejected it".
final class ChangePasswordValidationError extends ChangePasswordState {
  const ChangePasswordValidationError(this.reason);
  final ChangePasswordValidationReason reason;
}

enum ChangePasswordValidationReason { tooShort, isDefaultPassword, doesNotMatch }

final class ChangePasswordServerError extends ChangePasswordState {
  const ChangePasswordServerError(this.failure);
  final Failure failure;
}
