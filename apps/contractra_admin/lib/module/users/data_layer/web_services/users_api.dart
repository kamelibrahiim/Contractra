import 'package:dio/dio.dart';

/// Thin wrapper over the endpoints in claude-code-prompt-02-frontend.md.
/// No published contract exists yet (frontend-auth-and-users-plan.md,
/// gap G1) — kept separate from [DioUsersRepository] so a real contract
/// only changes this file's request/response shapes.
class UsersApi {
  UsersApi(this._dio);

  final Dio _dio;

  Future<Response<dynamic>> list({
    required int page,
    required int pageSize,
    String? search,
    String? status,
  }) =>
      _dio.get('/api/users', queryParameters: {
        'page': page,
        'pageSize': pageSize,
        if (search != null && search.isNotEmpty) 'search': search,
        if (status != null) 'status': status,
      });

  Future<Response<dynamic>> getById(String id) => _dio.get('/api/users/$id');

  Future<Response<dynamic>> listRoles() => _dio.get('/api/roles');

  Future<Response<dynamic>> create({
    required String loginIdentifier,
    required List<String> roleCodes,
  }) =>
      _dio.post('/api/users', data: {
        'loginIdentifier': loginIdentifier,
        'roleCodes': roleCodes,
      });

  Future<Response<dynamic>> disable(String id) => _dio.post('/api/users/$id/disable');

  Future<Response<dynamic>> reactivate(String id) => _dio.post('/api/users/$id/reactivate');

  Future<Response<dynamic>> resetPassword(String id) => _dio.post('/api/users/$id/reset-password');
}
