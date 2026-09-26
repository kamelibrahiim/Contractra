import '../../../../core/network/failures.dart';
import '../models/role_option.dart';
import '../models/user_page.dart';
import '../models/user_summary.dart';

sealed class UsersResult<T> {
  const UsersResult();
}

final class UsersSuccess<T> extends UsersResult<T> {
  const UsersSuccess(this.value);
  final T value;
}

final class UsersError<T> extends UsersResult<T> {
  const UsersError(this.failure);
  final Failure failure;
}

abstract class UsersRepository {
  Future<UsersResult<UserPage>> list({
    required int page,
    required int pageSize,
    String? search,
    UserAccountStatus? status,
  });

  Future<UsersResult<UserSummary>> getById(String id);

  Future<UsersResult<List<RoleOption>>> listRoles();

  /// FR-AUTH-003: at least one role is required — enforced by the caller
  /// before this is invoked, and again here as the source of truth.
  Future<UsersResult<UserSummary>> create({
    required String loginIdentifier,
    required List<String> roleCodes,
  });

  Future<UsersResult<void>> disable(String id);

  Future<UsersResult<void>> reactivate(String id);

  /// FR-AUTH-004: there is no delete for users — disable only, never remove.
  Future<UsersResult<void>> resetPassword(String id);
}
