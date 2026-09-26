import 'dart:math';

import '../../../../core/network/failures.dart';
import '../models/role_option.dart';
import '../models/user_page.dart';
import '../models/user_summary.dart';
import 'users_repository.dart';

/// In-memory stand-in for the real backend, used while no users API exists
/// (frontend-auth-and-users-plan.md, gap G1). Seed data is placeholder,
/// clearly not real records — enough rows to exercise paging.
class MockUsersRepository implements UsersRepository {
  MockUsersRepository() {
    _users.addAll([
      _MutableUser(id: 'u-1', loginIdentifier: 'admin', roles: ['Admin'], status: UserAccountStatus.active),
      _MutableUser(id: 'u-2', loginIdentifier: 'accountant', roles: ['Accountant'], status: UserAccountStatus.active),
      _MutableUser(id: 'u-3', loginIdentifier: 'office.mona', roles: ['OfficeStaff'], status: UserAccountStatus.active),
      _MutableUser(id: 'u-4', loginIdentifier: 'eng.ahmed', roles: ['Engineer'], status: UserAccountStatus.active),
      _MutableUser(id: 'u-5', loginIdentifier: 'eng.sara', roles: ['Engineer'], status: UserAccountStatus.active),
      _MutableUser(id: 'u-6', loginIdentifier: 'disabled.user', roles: ['Engineer'], status: UserAccountStatus.disabled),
      _MutableUser(id: 'u-7', loginIdentifier: 'office.hassan', roles: ['OfficeStaff'], status: UserAccountStatus.active),
      _MutableUser(id: 'u-8', loginIdentifier: 'eng.laila', roles: ['Engineer'], status: UserAccountStatus.disabled),
    ]);
  }

  final List<_MutableUser> _users = [];
  final _random = Random();

  static const _roles = [
    RoleOption(code: 'Admin', displayName: 'Admin'),
    RoleOption(code: 'Accountant', displayName: 'Accountant'),
    RoleOption(code: 'OfficeStaff', displayName: 'Office Staff'),
    RoleOption(code: 'Engineer', displayName: 'Engineer'),
  ];

  @override
  Future<UsersResult<UserPage>> list({
    required int page,
    required int pageSize,
    String? search,
    UserAccountStatus? status,
  }) async {
    await Future<void>.delayed(const Duration(milliseconds: 200));

    var filtered = _users.where((u) {
      final matchesSearch = search == null ||
          search.isEmpty ||
          u.loginIdentifier.toLowerCase().contains(search.toLowerCase());
      final matchesStatus = status == null || u.status == status;
      return matchesSearch && matchesStatus;
    }).toList();

    final start = (page - 1) * pageSize;
    final pageItems = start >= filtered.length
        ? <_MutableUser>[]
        : filtered.sublist(start, (start + pageSize).clamp(0, filtered.length));

    return UsersSuccess(UserPage(
      items: pageItems.map((u) => u.toSummary()).toList(),
      totalCount: filtered.length,
    ));
  }

  @override
  Future<UsersResult<UserSummary>> getById(String id) async {
    final user = _users.where((u) => u.id == id).firstOrNull;
    if (user == null) return const UsersError(UnknownFailure(code: 'users.not_found'));
    return UsersSuccess(user.toSummary());
  }

  @override
  Future<UsersResult<List<RoleOption>>> listRoles() async => const UsersSuccess(_roles);

  @override
  Future<UsersResult<UserSummary>> create({
    required String loginIdentifier,
    required List<String> roleCodes,
  }) async {
    await Future<void>.delayed(const Duration(milliseconds: 200));

    if (roleCodes.isEmpty) return const UsersError(RoleRequiredFailure());
    if (_users.any((u) => u.loginIdentifier.toLowerCase() == loginIdentifier.toLowerCase())) {
      return const UsersError(LoginIdentifierTakenFailure());
    }

    final user = _MutableUser(
      id: 'u-${_random.nextInt(1 << 32)}',
      loginIdentifier: loginIdentifier,
      roles: roleCodes,
      status: UserAccountStatus.active,
    );
    _users.add(user);
    return UsersSuccess(user.toSummary());
  }

  @override
  Future<UsersResult<void>> disable(String id) => _setStatus(id, UserAccountStatus.disabled);

  @override
  Future<UsersResult<void>> reactivate(String id) => _setStatus(id, UserAccountStatus.active);

  @override
  Future<UsersResult<void>> resetPassword(String id) async {
    final user = _users.where((u) => u.id == id).firstOrNull;
    if (user == null) return const UsersError(UnknownFailure(code: 'users.not_found'));
    // Mock has no password field to reset; the call itself is what's tested.
    return const UsersSuccess(null);
  }

  Future<UsersResult<void>> _setStatus(String id, UserAccountStatus status) async {
    final user = _users.where((u) => u.id == id).firstOrNull;
    if (user == null) return const UsersError(UnknownFailure(code: 'users.not_found'));
    user.status = status;
    return const UsersSuccess(null);
  }
}

class _MutableUser {
  _MutableUser({
    required this.id,
    required this.loginIdentifier,
    required this.roles,
    required this.status,
  });

  final String id;
  final String loginIdentifier;
  final List<String> roles;
  UserAccountStatus status;

  UserSummary toSummary() => UserSummary(
        id: id,
        loginIdentifier: loginIdentifier,
        roles: roles,
        status: status,
      );
}

extension<T> on Iterable<T> {
  T? get firstOrNull => isEmpty ? null : first;
}
