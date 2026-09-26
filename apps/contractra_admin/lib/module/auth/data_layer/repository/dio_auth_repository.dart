import 'package:dio/dio.dart';

import '../../../../core/network/problem_details_mapper.dart';
import '../../../../core/network/token_storage.dart';
import '../models/current_user.dart';
import '../models/login_result.dart';
import '../web_services/auth_api.dart';
import 'auth_repository.dart';

/// Unverified against a real server — no API contract exists yet
/// (frontend-auth-and-users-plan.md, gap G1). Request/response shapes
/// follow claude-code-prompt-02-frontend.md and are isolated to this file
/// and [AuthApi] so the eventual real contract is a local change.
class DioAuthRepository implements AuthRepository {
  DioAuthRepository(this._api, this._tokenStorage);

  final AuthApi _api;
  final TokenStorage _tokenStorage;

  @override
  Future<AuthResult<LoginResult>> login(String identifier, String password) async {
    try {
      final response = await _api.login(identifier, password);
      final result = LoginResult.fromJson(response.data as Map<String, dynamic>);
      _tokenStorage.accessToken = result.accessToken;
      await _tokenStorage.writeRefreshToken(result.refreshToken);
      return AuthSuccess(result);
    } on DioException catch (e) {
      return AuthError(ProblemDetailsMapper.fromDioException(e));
    }
  }

  @override
  Future<String> refresh() async {
    final refreshToken = await _tokenStorage.readRefreshToken();
    if (refreshToken == null) throw StateError('No refresh token to refresh with.');

    final response = await _api.refresh(refreshToken);
    final data = response.data as Map<String, dynamic>;
    final newAccessToken = data['accessToken'] as String;
    final newRefreshToken = data['refreshToken'] as String;
    await _tokenStorage.writeRefreshToken(newRefreshToken);
    return newAccessToken;
  }

  @override
  Future<void> logout() async {
    try {
      await _api.logout();
    } on DioException {
      // Best-effort: the session is cleared locally regardless.
    } finally {
      await _tokenStorage.clear();
    }
  }

  @override
  Future<AuthResult<CurrentUser>> me() async {
    try {
      final response = await _api.me();
      return AuthSuccess(CurrentUser.fromJson(response.data as Map<String, dynamic>));
    } on DioException catch (e) {
      return AuthError(ProblemDetailsMapper.fromDioException(e));
    }
  }

  @override
  Future<AuthResult<void>> changePassword({
    required String currentPassword,
    required String newPassword,
  }) async {
    try {
      await _api.changePassword(currentPassword: currentPassword, newPassword: newPassword);
      return const AuthSuccess(null);
    } on DioException catch (e) {
      return AuthError(ProblemDetailsMapper.fromDioException(e));
    }
  }
}
