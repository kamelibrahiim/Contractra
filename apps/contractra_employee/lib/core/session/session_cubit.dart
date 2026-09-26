import 'package:bloc/bloc.dart';

import '../../module/auth/data_layer/models/current_user.dart';
import '../../module/auth/data_layer/repository/auth_repository.dart';

part 'session_state.dart';

/// Single source of truth for "who is signed in and do they need to change
/// their password". Router guards and every screen that gates on auth read
/// this instead of calling AuthRepository themselves.
class SessionCubit extends Cubit<SessionState> {
  SessionCubit(this._authRepository) : super(const SessionUnknown());

  final AuthRepository _authRepository;

  /// Called once at startup (from splash) to restore an existing session
  /// via the refresh token, then `/auth/me`. Never throws.
  Future<void> restore() async {
    try {
      await _authRepository.refresh();
    } catch (_) {
      emit(const SessionUnauthenticated());
      return;
    }
    await _loadCurrentUser();
  }

  Future<void> login(String identifier, String password) async {
    emit(const SessionAuthenticating());
    final result = await _authRepository.login(identifier, password);
    switch (result) {
      case AuthSuccess():
        await _loadCurrentUser();
      case AuthError():
        emit(const SessionUnauthenticated());
    }
  }

  /// Called after a successful change-password call so the session
  /// reflects mustChangePassword having cleared, without a full /me
  /// round-trip.
  void passwordChanged() {
    final current = state;
    if (current is SessionPasswordChangeRequired) {
      emit(SessionAuthenticated(CurrentUser(
        id: current.user.id,
        loginIdentifier: current.user.loginIdentifier,
        roles: current.user.roles,
        permissions: current.user.permissions,
        mustChangePassword: false,
      )));
    }
  }

  Future<void> logout() async {
    await _authRepository.logout();
    emit(const SessionUnauthenticated());
  }

  Future<void> _loadCurrentUser() async {
    final meResult = await _authRepository.me();
    switch (meResult) {
      case AuthSuccess(value: final user):
        emit(user.mustChangePassword
            ? SessionPasswordChangeRequired(user)
            : SessionAuthenticated(user));
      case AuthError():
        emit(const SessionUnauthenticated());
    }
  }
}
