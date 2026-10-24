import 'package:flutter_test/flutter_test.dart';

import 'package:contractra_admin/core/network/failures.dart';
import 'package:contractra_admin/module/users/data_layer/models/user_summary.dart';
import 'package:contractra_admin/module/users/data_layer/repository/mock_users_repository.dart';
import 'package:contractra_admin/module/users/data_layer/repository/users_repository.dart';

void main() {
  late MockUsersRepository repository;

  setUp(() => repository = MockUsersRepository());

  group('list', () {
    test('paginates the seeded users', () async {
      final firstPage = await repository.list(page: 1, pageSize: 3);
      expect(firstPage, isA<UsersSuccess<dynamic>>());
      final page = (firstPage as UsersSuccess).value as dynamic;
      expect(page.items.length, 3);
      expect(page.totalCount, 8);
    });

    test('filters by a search term against the login identifier', () async {
      final result = await repository.list(page: 1, pageSize: 10, search: 'eng.');
      final page = (result as UsersSuccess).value as dynamic;
      expect(page.totalCount, 3);
    });

    test('filters by status', () async {
      final result = await repository.list(page: 1, pageSize: 10, status: UserAccountStatus.disabled);
      final page = (result as UsersSuccess).value as dynamic;
      expect(page.totalCount, 2);
    });
  });

  group('listRoles', () {
    test('returns the 4 documented roles', () async {
      final result = await repository.listRoles();
      expect((result as UsersSuccess).value.length, 4);
    });
  });

  group('create', () {
    test('rejects an empty role list (FR-AUTH-003)', () async {
      final result = await repository.create(loginIdentifier: 'new.user', roleCodes: []);
      expect(result, isA<UsersError<dynamic>>());
      expect((result as UsersError).failure, isA<RoleRequiredFailure>());
    });

    test('rejects a login identifier that is already taken', () async {
      final result = await repository.create(loginIdentifier: 'admin', roleCodes: ['Engineer']);
      expect(result, isA<UsersError<dynamic>>());
      expect((result as UsersError).failure, isA<LoginIdentifierTakenFailure>());
    });

    test('creates a new active user and it shows up in list()', () async {
      final result = await repository.create(loginIdentifier: 'new.engineer', roleCodes: ['Engineer']);
      expect(result, isA<UsersSuccess<dynamic>>());

      final page = (await repository.list(page: 1, pageSize: 20) as UsersSuccess).value as dynamic;
      expect(page.items.any((u) => u.loginIdentifier == 'new.engineer'), isTrue);
    });
  });

  group('account actions', () {
    test('disable then reactivate round-trips the status', () async {
      final disableResult = await repository.disable('u-1');
      expect(disableResult, isA<UsersSuccess<dynamic>>());
      var user = (await repository.getById('u-1') as UsersSuccess).value as dynamic;
      expect(user.status, UserAccountStatus.disabled);

      final reactivateResult = await repository.reactivate('u-1');
      expect(reactivateResult, isA<UsersSuccess<dynamic>>());
      user = (await repository.getById('u-1') as UsersSuccess).value as dynamic;
      expect(user.status, UserAccountStatus.active);
    });

    test('resetPassword succeeds for an existing user', () async {
      final result = await repository.resetPassword('u-1');
      expect(result, isA<UsersSuccess<dynamic>>());
    });

    test('an action on an unknown id fails', () async {
      final result = await repository.disable('does-not-exist');
      expect(result, isA<UsersError<dynamic>>());
    });
  });
}
