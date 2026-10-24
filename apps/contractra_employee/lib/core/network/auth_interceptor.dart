import 'package:dio/dio.dart';

import 'token_storage.dart';

/// Attaches the access token to every request. On a 401 it refreshes the
/// session once — concurrent 401s share that single refresh instead of each
/// starting their own — then retries the original request. If the refresh
/// itself fails, [onRefreshFailed] runs (the session is cleared) and the
/// original 401 is passed through.
class AuthInterceptor extends Interceptor {
  AuthInterceptor({
    required this.tokenStorage,
    required this.refresh,
    required this.onRefreshFailed,
    required this.retry,
  });

  final TokenStorage tokenStorage;

  /// Calls `/api/auth/refresh` and returns the new access token, or throws.
  final Future<String> Function() refresh;

  final Future<void> Function() onRefreshFailed;
  final Future<Response<dynamic>> Function(RequestOptions options) retry;

  Future<String>? _refreshInFlight;

  @override
  void onRequest(RequestOptions options, RequestInterceptorHandler handler) {
    final token = tokenStorage.accessToken;
    if (token != null) {
      options.headers['Authorization'] = 'Bearer $token';
    }
    handler.next(options);
  }

  @override
  void onError(DioException err, ErrorInterceptorHandler handler) async {
    final isAuthRequest = err.requestOptions.extra['isAuthRequest'] == true;
    if (err.response?.statusCode != 401 || isAuthRequest) {
      handler.next(err);
      return;
    }

    try {
      final newToken = await (_refreshInFlight ??= _doRefresh());
      final response = await retry(err.requestOptions..headers['Authorization'] = 'Bearer $newToken');
      handler.resolve(response);
    } catch (_) {
      await onRefreshFailed();
      handler.next(err);
    }
  }

  Future<String> _doRefresh() async {
    try {
      final token = await refresh();
      tokenStorage.accessToken = token;
      return token;
    } finally {
      _refreshInFlight = null;
    }
  }
}
