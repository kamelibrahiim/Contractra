import 'package:dio/dio.dart';

/// Thin wrapper over the endpoints in claude-code-prompt-02-frontend.md.
/// Kept separate from [DioAuthRepository] so the eventual real contract
/// only changes this file's request/response shapes.
class AuthApi {
  AuthApi(this._dio);

  final Dio _dio;

  Future<Response<dynamic>> login(String identifier, String password) => _dio.post(
        '/api/auth/login',
        data: {'loginIdentifier': identifier, 'password': password},
        options: Options(extra: {'isAuthRequest': true}),
      );

  Future<Response<dynamic>> refresh(String refreshToken) => _dio.post(
        '/api/auth/refresh',
        data: {'refreshToken': refreshToken},
        options: Options(extra: {'isAuthRequest': true}),
      );

  Future<Response<dynamic>> logout() => _dio.post('/api/auth/logout');

  Future<Response<dynamic>> me() => _dio.get('/api/auth/me');

  Future<Response<dynamic>> changePassword({
    required String currentPassword,
    required String newPassword,
  }) =>
      _dio.post(
        '/api/auth/change-password',
        data: {'currentPassword': currentPassword, 'newPassword': newPassword},
      );
}
