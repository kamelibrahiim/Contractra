import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

import 'package:contractra_admin/core/network/failures.dart';
import 'package:contractra_admin/module/users/data_layer/models/user_summary.dart';
import 'package:contractra_admin/module/users/data_layer/repository/dio_users_repository.dart';
import 'package:contractra_admin/module/users/data_layer/repository/users_repository.dart';
import 'package:contractra_admin/module/users/data_layer/web_services/users_api.dart';

class _MockUsersApi extends Mock implements UsersApi {}

Response<dynamic> _response(Object? data, {int statusCode = 200}) => Response(
      requestOptions: RequestOptions(path: '/test'),
      statusCode: statusCode,
      data: data,
    );

void main() {
  late _MockUsersApi api;
  late DioUsersRepository repository;

  setUp(() {
    api = _MockUsersApi();
    repository = DioUsersRepository(api);
  });

  group('list', () {
    test('parses a page and passes the status filter through', () async {
      when(() => api.list(page: 1, pageSize: 20, search: null, status: 'Disabled')).thenAnswer(
        (_) async => _response({
          'items': [
            {'id': 'u-1', 'loginIdentifier': 'disabled.user', 'roles': ['Engineer'], 'status': 'Disabled'},
          ],
          'totalCount': 1,
        }),
      );

      final result = await repository.list(page: 1, pageSize: 20, status: UserAccountStatus.disabled);

      expect(result, isA<UsersSuccess<dynamic>>());
      expect((result as UsersSuccess).value.items.single.status, UserAccountStatus.disabled);
    });
  });

  group('listRoles', () {
    test('parses a list of role options', () async {
      when(() => api.listRoles()).thenAnswer(
        (_) async => _response([
          {'code': 'Admin', 'displayName': 'Admin'},
        ]),
      );

      final result = await repository.listRoles();

      expect(result, isA<UsersSuccess<dynamic>>());
      expect((result as UsersSuccess).value.single.code, 'Admin');
    });
  });

  group('create', () {
    test('maps users.role_required from the server', () async {
      when(() => api.create(loginIdentifier: 'new.user', roleCodes: [])).thenThrow(
        DioException(
          requestOptions: RequestOptions(path: '/api/users'),
          response: _response({'code': 'users.role_required'}, statusCode: 400),
        ),
      );

      final result = await repository.create(loginIdentifier: 'new.user', roleCodes: []);

      expect(result, isA<UsersError<dynamic>>());
      expect((result as UsersError).failure, isA<RoleRequiredFailure>());
    });

    test('maps users.login_identifier_taken from the server', () async {
      when(() => api.create(loginIdentifier: 'admin', roleCodes: ['Engineer'])).thenThrow(
        DioException(
          requestOptions: RequestOptions(path: '/api/users'),
          response: _response({'code': 'users.login_identifier_taken'}, statusCode: 409),
        ),
      );

      final result = await repository.create(loginIdentifier: 'admin', roleCodes: ['Engineer']);

      expect(result, isA<UsersError<dynamic>>());
      expect((result as UsersError).failure, isA<LoginIdentifierTakenFailure>());
    });
  });

  group('account actions', () {
    test('disable succeeds', () async {
      when(() => api.disable('u-1')).thenAnswer((_) async => _response(null));
      expect(await repository.disable('u-1'), isA<UsersSuccess<dynamic>>());
    });

    test('reactivate succeeds', () async {
      when(() => api.reactivate('u-1')).thenAnswer((_) async => _response(null));
      expect(await repository.reactivate('u-1'), isA<UsersSuccess<dynamic>>());
    });

    test('resetPassword succeeds', () async {
      when(() => api.resetPassword('u-1')).thenAnswer((_) async => _response(null));
      expect(await repository.resetPassword('u-1'), isA<UsersSuccess<dynamic>>());
    });
  });
}
