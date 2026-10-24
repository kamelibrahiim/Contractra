import 'package:bloc/bloc.dart';

import '../../../../core/network/failures.dart';
import '../../../../core/session/session_cubit.dart';
import '../../data_layer/repository/auth_repository.dart';

part 'change_password_state.dart';

/// The default password every new/reset account starts with
/// (claude-code-prompt-02-frontend.md). Not documented in any authoritative
/// source (frontend-auth-and-users-plan.md, gap G2) — implemented as
/// specified there pending confirmation.
const defaultPassword = '12345678';
const minPasswordLength = 8;

class ChangePasswordCubit extends Cubit<ChangePasswordState> {
  ChangePasswordCubit(this._authRepository, this._sessionCubit) : super(const ChangePasswordIdle());

  final AuthRepository _authRepository;
  final SessionCubit _sessionCubit;

  Future<void> submit({
    required String currentPassword,
    required String newPassword,
    required String confirmPassword,
  }) async {
    if (state is ChangePasswordSubmitting) return;

    if (newPassword.length < minPasswordLength) {
      emit(const ChangePasswordValidationError(ChangePasswordValidationReason.tooShort));
      return;
    }
    if (newPassword == defaultPassword) {
      emit(const ChangePasswordValidationError(ChangePasswordValidationReason.isDefaultPassword));
      return;
    }
    if (newPassword != confirmPassword) {
      emit(const ChangePasswordValidationError(ChangePasswordValidationReason.doesNotMatch));
      return;
    }

    emit(const ChangePasswordSubmitting());
    final result = await _authRepository.changePassword(
      currentPassword: currentPassword,
      newPassword: newPassword,
    );

    switch (result) {
      case AuthSuccess():
        _sessionCubit.passwordChanged();
        emit(const ChangePasswordSuccess());
      case AuthError(failure: final failure):
        emit(ChangePasswordServerError(failure));
    }
  }
}
