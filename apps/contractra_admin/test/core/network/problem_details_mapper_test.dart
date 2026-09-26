import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:contractra_admin/core/network/failures.dart';
import 'package:contractra_admin/core/network/problem_details_mapper.dart';

DioException _errorWith({int? statusCode, Object? data}) {
  final requestOptions = RequestOptions(path: '/test');
  return DioException(
    requestOptions: requestOptions,
    type: DioExceptionType.badResponse,
    response: statusCode == null
        ? null
        : Response(requestOptions: requestOptions, statusCode: statusCode, data: data),
  );
}

void main() {
  group('ProblemDetailsMapper', () {
    test('maps auth.invalid_credentials', () {
      final failure = ProblemDetailsMapper.fromDioException(
        _errorWith(statusCode: 401, data: {'code': 'auth.invalid_credentials'}),
      );
      expect(failure, isA<InvalidCredentialsFailure>());
    });

    test('maps auth.password_change_required', () {
      final failure = ProblemDetailsMapper.fromDioException(
        _errorWith(statusCode: 403, data: {'code': 'auth.password_change_required'}),
      );
      expect(failure, isA<PasswordChangeRequiredFailure>());
    });

    test('maps users.login_identifier_taken', () {
      final failure = ProblemDetailsMapper.fromDioException(
        _errorWith(statusCode: 409, data: {'code': 'users.login_identifier_taken'}),
      );
      expect(failure, isA<LoginIdentifierTakenFailure>());
    });

    test('maps users.role_required', () {
      final failure = ProblemDetailsMapper.fromDioException(
        _errorWith(statusCode: 400, data: {'code': 'users.role_required'}),
      );
      expect(failure, isA<RoleRequiredFailure>());
    });

    test('maps users.disabled', () {
      final failure = ProblemDetailsMapper.fromDioException(
        _errorWith(statusCode: 403, data: {'code': 'users.disabled'}),
      );
      expect(failure, isA<UserDisabledFailure>());
    });

    test('maps a bare 401 with no known code to UnauthorizedFailure', () {
      final failure = ProblemDetailsMapper.fromDioException(_errorWith(statusCode: 401, data: {}));
      expect(failure, isA<UnauthorizedFailure>());
    });

    test('maps a bare 403 with no known code to ForbiddenFailure', () {
      final failure = ProblemDetailsMapper.fromDioException(_errorWith(statusCode: 403, data: {}));
      expect(failure, isA<ForbiddenFailure>());
    });

    test('maps an unrecognized code to UnknownFailure, keeping the code for diagnostics', () {
      final failure = ProblemDetailsMapper.fromDioException(
        _errorWith(statusCode: 500, data: {'code': 'something.new'}),
      );
      expect(failure, isA<UnknownFailure>());
      expect((failure as UnknownFailure).code, 'something.new');
    });

    test('maps a response with no ProblemDetails body to UnknownFailure with a null code', () {
      final failure = ProblemDetailsMapper.fromDioException(_errorWith(statusCode: 500, data: null));
      expect(failure, isA<UnknownFailure>());
      expect((failure as UnknownFailure).code, isNull);
    });

    test('maps connection errors to NetworkFailure', () {
      final requestOptions = RequestOptions(path: '/test');
      final failure = ProblemDetailsMapper.fromDioException(
        DioException(requestOptions: requestOptions, type: DioExceptionType.connectionError),
      );
      expect(failure, isA<NetworkFailure>());
    });
  });
}
