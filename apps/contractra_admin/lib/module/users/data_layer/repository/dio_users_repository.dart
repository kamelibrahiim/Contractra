import 'package:dio/dio.dart';

import '../../../../core/network/problem_details_mapper.dart';
import '../models/role_option.dart';
import '../models/user_page.dart';
import '../models/user_summary.dart';
import '../web_services/users_api.dart';
import 'users_repository.dart';

/// Unverified against a real server — no API contract exists yet
/// (frontend-auth-and-users-plan.md, gap G1). Request/response shapes
/// follow claude-code-prompt-02-frontend.md and are isolated to this file
/// and [UsersApi] so the eventual real contract is a local change.
class DioUsersRepository implements UsersRepository {
  DioUsersRepository(this._api);

  final UsersApi _api;

  @override
  Future<UsersResult<UserPage>> list({
    required int page,
    required int pageSize,
    String? search,
    UserAccountStatus? status,
  }) async {
    try {
      final response = await _api.list(
        page: page,
        pageSize: pageSize,
        search: search,
        status: status == null ? null : (status == UserAccountStatus.disabled ? 'Disabled' : 'Active'),
      );
      return UsersSuccess(UserPage.fromJson(response.data as Map<String, dynamic>));
    } on DioException catch (e) {
      return UsersError(ProblemDetailsMapper.fromDioException(e));
    }
  }

  @override
  Future<UsersResult<UserSummary>> getById(String id) async {
    try {
      final response = await _api.getById(id);
      return UsersSuccess(UserSummary.fromJson(response.data as Map<String, dynamic>));
    } on DioException catch (e) {
      return UsersError(ProblemDetailsMapper.fromDioException(e));
    }
  }

  @override
  Future<UsersResult<List<RoleOption>>> listRoles() async {
    try {
      final response = await _api.listRoles();
      final roles = (response.data as List)
          .map((e) => RoleOption.fromJson(e as Map<String, dynamic>))
          .toList();
      return UsersSuccess(roles);
    } on DioException catch (e) {
      return UsersError(ProblemDetailsMapper.fromDioException(e));
    }
  }

  @override
  Future<UsersResult<UserSummary>> create({
    required String loginIdentifier,
    required List<String> roleCodes,
  }) async {
    try {
      final response = await _api.create(loginIdentifier: loginIdentifier, roleCodes: roleCodes);
      return UsersSuccess(UserSummary.fromJson(response.data as Map<String, dynamic>));
    } on DioException catch (e) {
      return UsersError(ProblemDetailsMapper.fromDioException(e));
    }
  }

  @override
  Future<UsersResult<void>> disable(String id) => _simpleCall(() => _api.disable(id));

  @override
  Future<UsersResult<void>> reactivate(String id) => _simpleCall(() => _api.reactivate(id));

  @override
  Future<UsersResult<void>> resetPassword(String id) => _simpleCall(() => _api.resetPassword(id));

  Future<UsersResult<void>> _simpleCall(Future<Response<dynamic>> Function() call) async {
    try {
      await call();
      return const UsersSuccess(null);
    } on DioException catch (e) {
      return UsersError(ProblemDetailsMapper.fromDioException(e));
    }
  }
}
