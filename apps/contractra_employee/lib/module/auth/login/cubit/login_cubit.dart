import 'package:bloc/bloc.dart';

import '../../../../core/network/failures.dart';
import '../../../../core/session/session_cubit.dart';

part 'login_state.dart';

/// Delegates the actual call to [SessionCubit] (shared with session
/// restore) and translates its outcome into UI-facing states. Never
/// imports Dio/fast_dio directly — network access goes through
/// AuthRepository, behind SessionCubit.
class LoginCubit extends Cubit<LoginState> {
  LoginCubit(this._sessionCubit) : super(const LoginIdle());

  final SessionCubit _sessionCubit;

  Future<void> submit(String identifier, String password) async {
    if (state is LoginSubmitting) return;

    if (identifier.trim().isEmpty || password.isEmpty) {
      emit(const LoginValidationError());
      return;
    }

    emit(const LoginSubmitting());
    await _sessionCubit.login(identifier, password);

    final result = _sessionCubit.state;
    if (isClosed) return;

    switch (result) {
      case SessionAuthenticated():
      case SessionPasswordChangeRequired():
        emit(const LoginSucceeded());
      case SessionUnauthenticated():
        emit(_failureStateFor(_sessionCubit.lastLoginFailure));
      case SessionUnknown():
      case SessionAuthenticating():
        // Should not happen: login() always resolves to one of the above.
        emit(const LoginIdle());
    }
  }

  LoginState _failureStateFor(Failure? failure) => switch (failure) {
        UserDisabledFailure() => const LoginAccountDisabled(),
        NetworkFailure() => const LoginNetworkError(),
        _ => const LoginInvalidCredentials(),
      };
}
