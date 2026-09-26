import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

import 'package:contractra_employee/core/network/failures.dart';
import 'package:contractra_employee/core/network/in_memory_token_storage.dart';
import 'package:contractra_employee/module/auth/data_layer/repository/auth_repository.dart';
import 'package:contractra_employee/module/auth/data_layer/repository/dio_auth_repository.dart';
import 'package:contractra_employee/module/auth/data_layer/web_services/auth_api.dart';

class _MockAuthApi extends Mock implements AuthApi {}

Response<dynamic> _response(Object? data, {int statusCode = 200}) => Response(
      requestOptions: RequestOptions(path: '/test'),
      statusCode: statusCode,
      data: data,
    );

void main() {
  late _MockAuthApi api;
  late InMemoryTokenStorage tokenStorage;
  late DioAuthRepository repository;

  setUp(() {
    api = _MockAuthApi();
    tokenStorage = InMemoryTokenStorage();
    repository = DioAuthRepository(api, tokenStorage);
  });

  group('login', () {
    test('stores both tokens and returns success on 200', () async {
      when(() => api.login('admin', '12345678')).thenAnswer(
        (_) async => _response({
          'accessToken': 'access-1',
          'refreshToken': 'refresh-1',
          'mustChangePassword': true,
        }),
      );

      final result = await repository.login('admin', '12345678');

      expect(result, isA<AuthSuccess<dynamic>>());
      expect(tokenStorage.accessToken, 'access-1');
      expect(await tokenStorage.readRefreshToken(), 'refresh-1');
    });

    test('maps invalid credentials without storing any token', () async {
      when(() => api.login('admin', 'wrong')).thenThrow(
        DioException(
          requestOptions: RequestOptions(path: '/api/auth/login'),
          response: _response({'code': 'auth.invalid_credentials'}, statusCode: 401),
        ),
      );

      final result = await repository.login('admin', 'wrong');

      expect(result, isA<AuthError<dynamic>>());
      expect((result as AuthError).failure, isA<InvalidCredentialsFailure>());
      expect(tokenStorage.accessToken, isNull);
    });
  });

  group('refresh', () {
    test('uses the stored refresh token and persists the new one', () async {
      await tokenStorage.writeRefreshToken('old-refresh');
      when(() => api.refresh('old-refresh')).thenAnswer(
        (_) async => _response({'accessToken': 'new-access', 'refreshToken': 'new-refresh'}),
      );

      final newAccessToken = await repository.refresh();

      expect(newAccessToken, 'new-access');
      expect(await tokenStorage.readRefreshToken(), 'new-refresh');
    });

    test('throws when there is no refresh token to use', () async {
      await expectLater(repository.refresh(), throwsStateError);
    });
  });

  group('logout', () {
    test('clears local tokens even if the server call fails', () async {
      tokenStorage.accessToken = 'access-1';
      await tokenStorage.writeRefreshToken('refresh-1');
      when(() => api.logout()).thenThrow(
        DioException(requestOptions: RequestOptions(path: '/api/auth/logout')),
      );

      await repository.logout();

      expect(tokenStorage.accessToken, isNull);
      expect(await tokenStorage.readRefreshToken(), isNull);
    });
  });

  group('me', () {
    test('parses a CurrentUser on success', () async {
      when(() => api.me()).thenAnswer(
        (_) async => _response({
          'id': 'u1',
          'loginIdentifier': 'admin',
          'roles': ['Admin'],
          'permissions': ['Users.Manage'],
          'mustChangePassword': false,
        }),
      );

      final result = await repository.me();

      expect(result, isA<AuthSuccess<dynamic>>());
    });
  });

  group('changePassword', () {
    test('maps a server error to a Failure', () async {
      when(() => api.changePassword(currentPassword: '12345678', newPassword: '12345678'))
          .thenThrow(DioException(
        requestOptions: RequestOptions(path: '/api/auth/change-password'),
        response: _response({'code': 'unknown'}, statusCode: 400),
      ));

      final result = await repository.changePassword(
        currentPassword: '12345678',
        newPassword: '12345678',
      );

      expect(result, isA<AuthError<dynamic>>());
    });
  });
}
