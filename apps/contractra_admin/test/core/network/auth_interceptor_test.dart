import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:contractra_admin/core/network/auth_interceptor.dart';
import 'package:contractra_admin/core/network/in_memory_token_storage.dart';

class _FakeAdapter implements HttpClientAdapter {
  _FakeAdapter(this.responder);

  final Future<ResponsePayload> Function(RequestOptions options) responder;

  @override
  void close({bool force = false}) {}

  @override
  Future<ResponseBody> fetch(
    RequestOptions options,
    Stream<Uint8List>? requestStream,
    Future<void>? cancelFuture,
  ) async {
    final payload = await responder(options);
    return ResponseBody.fromString(
      payload.body,
      payload.statusCode,
      headers: {Headers.contentTypeHeader: [Headers.jsonContentType]},
    );
  }
}

class ResponsePayload {
  ResponsePayload(this.statusCode, this.body);
  final int statusCode;
  final String body;
}

void main() {
  group('AuthInterceptor', () {
    test('attaches the stored access token to outgoing requests', () async {
      final tokenStorage = InMemoryTokenStorage()..accessToken = 'access-1';
      String? seenAuthHeader;

      final dio = Dio(BaseOptions(baseUrl: 'https://api.test'));
      dio.httpClientAdapter = _FakeAdapter((options) async {
        seenAuthHeader = options.headers['Authorization'] as String?;
        return ResponsePayload(200, '{"ok":true}');
      });
      dio.interceptors.add(AuthInterceptor(
        tokenStorage: tokenStorage,
        refresh: () async => throw StateError('should not refresh'),
        onRefreshFailed: () async {},
        retry: (o) => dio.fetch(o),
      ));

      await dio.get('/ping');
      expect(seenAuthHeader, 'Bearer access-1');
    });

    test('on 401 it refreshes once, then retries and resolves with the new token', () async {
      final tokenStorage = InMemoryTokenStorage()..accessToken = 'expired';
      var refreshCalls = 0;
      var pingCalls = 0;
      String? tokenOnRetry;

      final dio = Dio(BaseOptions(baseUrl: 'https://api.test'));
      dio.httpClientAdapter = _FakeAdapter((options) async {
        if (options.path == '/ping') {
          pingCalls++;
          final header = options.headers['Authorization'] as String?;
          if (header == 'Bearer expired') {
            return ResponsePayload(401, '{"code":"auth.invalid_credentials"}');
          }
          tokenOnRetry = header;
          return ResponsePayload(200, '{"ok":true}');
        }
        throw StateError('unexpected path ${options.path}');
      });
      dio.interceptors.add(AuthInterceptor(
        tokenStorage: tokenStorage,
        refresh: () async {
          refreshCalls++;
          return 'fresh';
        },
        onRefreshFailed: () async {},
        retry: (o) => dio.fetch(o),
      ));

      final response = await dio.get('/ping');
      expect(response.statusCode, 200);
      expect(refreshCalls, 1);
      expect(pingCalls, 2);
      expect(tokenOnRetry, 'Bearer fresh');
      expect(tokenStorage.accessToken, 'fresh');
    });

    test('concurrent 401s share a single refresh', () async {
      final tokenStorage = InMemoryTokenStorage()..accessToken = 'expired';
      var refreshCalls = 0;

      final dio = Dio(BaseOptions(baseUrl: 'https://api.test'));
      dio.httpClientAdapter = _FakeAdapter((options) async {
        final header = options.headers['Authorization'] as String?;
        if (header == 'Bearer expired') {
          return ResponsePayload(401, '{"code":"auth.invalid_credentials"}');
        }
        return ResponsePayload(200, '{"ok":true}');
      });
      dio.interceptors.add(AuthInterceptor(
        tokenStorage: tokenStorage,
        refresh: () async {
          refreshCalls++;
          await Future<void>.delayed(const Duration(milliseconds: 20));
          return 'fresh';
        },
        onRefreshFailed: () async {},
        retry: (o) => dio.fetch(o),
      ));

      final results = await Future.wait([
        dio.get('/a'),
        dio.get('/b'),
        dio.get('/c'),
      ]);

      expect(refreshCalls, 1);
      expect(results.every((r) => r.statusCode == 200), isTrue);
    });

    test('clears the session and passes through the original 401 when refresh fails', () async {
      final tokenStorage = InMemoryTokenStorage()..accessToken = 'expired';
      var sessionCleared = false;

      final dio = Dio(BaseOptions(baseUrl: 'https://api.test'));
      dio.httpClientAdapter = _FakeAdapter((options) async {
        return ResponsePayload(401, '{"code":"auth.invalid_credentials"}');
      });
      dio.interceptors.add(AuthInterceptor(
        tokenStorage: tokenStorage,
        refresh: () async => throw DioException(requestOptions: RequestOptions(path: '/refresh')),
        onRefreshFailed: () async => sessionCleared = true,
        retry: (o) => dio.fetch(o),
      ));

      await expectLater(dio.get('/ping'), throwsA(isA<DioException>()));
      expect(sessionCleared, isTrue);
    });

    test('a 401 on the refresh request itself is not retried again', () async {
      final tokenStorage = InMemoryTokenStorage()..accessToken = 'expired';
      var refreshCalls = 0;

      final dio = Dio(BaseOptions(baseUrl: 'https://api.test'));
      dio.httpClientAdapter = _FakeAdapter((options) async {
        return ResponsePayload(401, '{"code":"auth.invalid_credentials"}');
      });
      dio.interceptors.add(AuthInterceptor(
        tokenStorage: tokenStorage,
        refresh: () async {
          refreshCalls++;
          throw DioException(requestOptions: RequestOptions(path: '/refresh'));
        },
        onRefreshFailed: () async {},
        retry: (o) => dio.fetch(o),
      ));

      await expectLater(
        dio.get('/refresh-like', options: Options(extra: {'isAuthRequest': true})),
        throwsA(isA<DioException>()),
      );
      expect(refreshCalls, 0);
    });
  });
}
