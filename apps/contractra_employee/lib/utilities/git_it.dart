import 'package:get_it/get_it.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../core/config/app_config.dart';
import '../core/network/api_client.dart';
import '../core/network/secure_token_storage.dart';
import '../core/network/token_storage.dart';
import '../module/auth/data_layer/repository/auth_repository.dart';
import '../module/auth/data_layer/repository/dio_auth_repository.dart';
import '../module/auth/data_layer/repository/mock_auth_repository.dart';
import '../module/auth/data_layer/web_services/auth_api.dart';

class GitIt {
  static final GetIt instance = GetIt.I..allowReassignment = true;

  static Future<void> initGitIt() async {
    final sharedPrefs = await SharedPreferences.getInstance();
    instance.registerLazySingleton<SharedPreferences>(() => sharedPrefs);

    final tokenStorage = SecureTokenStorage();
    instance.registerLazySingleton<TokenStorage>(() => tokenStorage);

    if (AppConfig.useMockApi) {
      instance.registerLazySingleton<AuthRepository>(() => MockAuthRepository(tokenStorage));
    } else {
      // Late-bound so the interceptor's refresh callback can call back into
      // the same repository instance once it exists (see below).
      late final AuthRepository authRepository;
      final dio = buildApiClient(
        tokenStorage: tokenStorage,
        refresh: () => authRepository.refresh(),
        onRefreshFailed: () => tokenStorage.clear(),
      );
      authRepository = DioAuthRepository(AuthApi(dio), tokenStorage);
      instance.registerLazySingleton<AuthRepository>(() => authRepository);
    }
  }
}
