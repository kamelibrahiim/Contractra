import 'package:dio/dio.dart';

import '../config/app_config.dart';
import 'auth_interceptor.dart';
import 'token_storage.dart';

/// Builds the Dio instance every repository shares. [refresh] and
/// [onRefreshFailed] are injected so this file has no direct dependency on
/// AuthRepository/SessionCubit (they depend on this instead).
Dio buildApiClient({
  required TokenStorage tokenStorage,
  required Future<String> Function() refresh,
  required Future<void> Function() onRefreshFailed,
}) {
  final dio = Dio(BaseOptions(
    baseUrl: AppConfig.apiBaseUrl,
    connectTimeout: const Duration(seconds: 10),
    receiveTimeout: const Duration(seconds: 10),
  ));

  dio.interceptors.add(AuthInterceptor(
    tokenStorage: tokenStorage,
    refresh: refresh,
    onRefreshFailed: onRefreshFailed,
    retry: (options) => dio.fetch(options),
  ));

  return dio;
}
