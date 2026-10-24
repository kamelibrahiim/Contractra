import 'dart:math';

import '../../../../core/network/failures.dart';
import '../../../../core/network/token_storage.dart';
import '../models/current_user.dart';
import '../models/login_result.dart';
import 'auth_repository.dart';

class _MockAccount {
  _MockAccount({
    required this.password,
    required this.user,
    required this.disabled,
  });

  String password;
  CurrentUser user;
  bool disabled;
}

/// In-memory stand-in for the real backend, used while no auth API exists
/// (frontend-auth-and-users-plan.md, gap G1). Seed accounts are placeholder
/// data, clearly not real records:
/// - `admin` / `12345678` — must change password on first login.
/// - `accountant` / `Accountant#2026` — normal session, Users.Manage absent.
/// - `disabled.user` / `12345678` — rejected with `users.disabled`.
class MockAuthRepository implements AuthRepository {
  MockAuthRepository(this._tokenStorage) {
    _accounts['admin'] = _MockAccount(
      password: '12345678',
      disabled: false,
      user: const CurrentUser(
        id: 'mock-user-admin',
        loginIdentifier: 'admin',
        roles: ['Admin'],
        permissions: ['Users.Manage', 'Permissions.Grant', 'Roles.Manage'],
        mustChangePassword: true,
      ),
    );
    _accounts['accountant'] = _MockAccount(
      password: 'Accountant#2026',
      disabled: false,
      user: const CurrentUser(
        id: 'mock-user-accountant',
        loginIdentifier: 'accountant',
        roles: ['Accountant'],
        permissions: ['Parties.View', 'Operations.View'],
        mustChangePassword: false,
      ),
    );
    _accounts['disabled.user'] = _MockAccount(
      password: '12345678',
      disabled: true,
      user: const CurrentUser(
        id: 'mock-user-disabled',
        loginIdentifier: 'disabled.user',
        roles: ['Engineer'],
        permissions: [],
        mustChangePassword: false,
      ),
    );
  }

  final Map<String, _MockAccount> _accounts = {};
  final TokenStorage _tokenStorage;
  final _random = Random();

  String? _sessionIdentifier;

  String _issueToken(String prefix) => '$prefix-${_random.nextInt(1 << 32)}';

  @override
  Future<AuthResult<LoginResult>> login(String identifier, String password) async {
    await Future<void>.delayed(const Duration(milliseconds: 300));

    final account = _accounts[identifier];
    if (account == null || account.password != password) {
      return const AuthError(InvalidCredentialsFailure());
    }
    if (account.disabled) {
      return const AuthError(UserDisabledFailure());
    }

    _sessionIdentifier = identifier;
    final result = LoginResult(
      accessToken: _issueToken('mock-access'),
      refreshToken: _issueToken('mock-refresh'),
      mustChangePassword: account.user.mustChangePassword,
    );
    _tokenStorage.accessToken = result.accessToken;
    await _tokenStorage.writeRefreshToken(result.refreshToken);
    return AuthSuccess(result);
  }

  @override
  Future<String> refresh() async {
    final refreshToken = await _tokenStorage.readRefreshToken();
    if (refreshToken == null || _sessionIdentifier == null) {
      throw StateError('No active mock session to refresh.');
    }
    await Future<void>.delayed(const Duration(milliseconds: 100));
    final newAccessToken = _issueToken('mock-access');
    await _tokenStorage.writeRefreshToken(_issueToken('mock-refresh'));
    return newAccessToken;
  }

  @override
  Future<void> logout() async {
    _sessionIdentifier = null;
    await _tokenStorage.clear();
  }

  @override
  Future<AuthResult<CurrentUser>> me() async {
    final identifier = _sessionIdentifier;
    if (identifier == null) return const AuthError(UnauthorizedFailure());

    final account = _accounts[identifier];
    if (account == null || account.disabled) return const AuthError(UnauthorizedFailure());
    return AuthSuccess(account.user);
  }

  @override
  Future<AuthResult<void>> changePassword({
    required String currentPassword,
    required String newPassword,
  }) async {
    final identifier = _sessionIdentifier;
    if (identifier == null) return const AuthError(UnauthorizedFailure());

    final account = _accounts[identifier];
    if (account == null || account.password != currentPassword) {
      return const AuthError(InvalidCredentialsFailure());
    }

    account.password = newPassword;
    account.user = CurrentUser(
      id: account.user.id,
      loginIdentifier: account.user.loginIdentifier,
      roles: account.user.roles,
      permissions: account.user.permissions,
      mustChangePassword: false,
    );
    return const AuthSuccess(null);
  }
}
