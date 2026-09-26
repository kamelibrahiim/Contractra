import 'package:bloc/bloc.dart';

part 'login_state.dart';

/// Local-only stub: validates that both fields are filled, simulates a
/// request, then returns to idle. Credentials are never stored in state,
/// logged, or persisted.
class LoginCubit extends Cubit<LoginState> {
  LoginCubit({this.simulatedDelay = const Duration(seconds: 1)})
      : super(const LoginIdle());

  final Duration simulatedDelay;

  Future<void> submit(String identifier, String password) async {
    if (state is LoginSubmitting) return;

    if (identifier.trim().isEmpty || password.isEmpty) {
      emit(const LoginValidationError());
      return;
    }

    emit(const LoginSubmitting());
    // TODO(sprint-2): call POST /auth/login once Users/Auth backend exists.
    await Future<void>.delayed(simulatedDelay);
    if (!isClosed) emit(const LoginIdle());
  }
}
