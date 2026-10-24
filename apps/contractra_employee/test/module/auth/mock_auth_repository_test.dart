import 'package:flutter_test/flutter_test.dart';

import 'package:contractra_employee/core/network/failures.dart';
import 'package:contractra_employee/core/network/in_memory_token_storage.dart';
import 'package:contractra_employee/module/auth/data_layer/repository/auth_repository.dart';
import 'package:contractra_employee/module/auth/data_layer/repository/mock_auth_repository.dart';

void main() {
  late InMemoryTokenStorage tokenStorage;
  late MockAuthRepository repository;

  setUp(() {
    tokenStorage = InMemoryTokenStorage();
    repository = MockAuthRepository(tokenStorage);
  });

  group('login', () {
    test('the seeded admin account must change its password', () async {
      final result = await repository.login('admin', '12345678');
      expect(result, isA<AuthSuccess<dynamic>>());
      expect((result as AuthSuccess).value.mustChangePassword, isTrue);
      expect(tokenStorage.accessToken, isNotNull);
    });

    test('the seeded accountant account does not need to change its password', () async {
      final result = await repository.login('accountant', 'Accountant#2026');
      expect(result, isA<AuthSuccess<dynamic>>());
      expect((result as AuthSuccess).value.mustChangePassword, isFalse);
    });

    test('an unknown identifier is rejected as invalid credentials', () async {
      final result = await repository.login('nobody', 'whatever');
      expect(result, isA<AuthError<dynamic>>());
      expect((result as AuthError).failure, isA<InvalidCredentialsFailure>());
    });

    test('the wrong password for a known identifier is rejected', () async {
      final result = await repository.login('admin', 'wrong');
      expect(result, isA<AuthError<dynamic>>());
      expect((result as AuthError).failure, isA<InvalidCredentialsFailure>());
    });

    test('a disabled account is rejected even with the correct password', () async {
      final result = await repository.login('disabled.user', '12345678');
      expect(result, isA<AuthError<dynamic>>());
      expect((result as AuthError).failure, isA<UserDisabledFailure>());
    });
  });

  group('session lifecycle', () {
    test('me fails before any login', () async {
      final result = await repository.me();
      expect(result, isA<AuthError<dynamic>>());
    });

    test('me succeeds after login and fails again after logout', () async {
      await repository.login('admin', '12345678');
      expect(await repository.me(), isA<AuthSuccess<dynamic>>());

      await repository.logout();

      expect(await repository.me(), isA<AuthError<dynamic>>());
      expect(tokenStorage.accessToken, isNull);
      expect(await tokenStorage.readRefreshToken(), isNull);
    });

    test('refresh issues a new access token for an active session', () async {
      await repository.login('admin', '12345678');
      final newToken = await repository.refresh();
      expect(newToken, isNotEmpty);
      expect(tokenStorage.accessToken, isNot(newToken));
    });
  });

  group('changePassword', () {
    test('clears mustChangePassword and allows login with the new password', () async {
      await repository.login('admin', '12345678');

      final changeResult = await repository.changePassword(
        currentPassword: '12345678',
        newPassword: 'NewPass#2026',
      );
      expect(changeResult, isA<AuthSuccess<dynamic>>());

      final meResult = await repository.me();
      expect((meResult as AuthSuccess).value.mustChangePassword, isFalse);

      await repository.logout();
      final loginResult = await repository.login('admin', 'NewPass#2026');
      expect(loginResult, isA<AuthSuccess<dynamic>>());
      expect((loginResult as AuthSuccess).value.mustChangePassword, isFalse);
    });

    test('rejects the wrong current password', () async {
      await repository.login('admin', '12345678');
      final result = await repository.changePassword(
        currentPassword: 'wrong',
        newPassword: 'NewPass#2026',
      );
      expect(result, isA<AuthError<dynamic>>());
    });
  });
}
