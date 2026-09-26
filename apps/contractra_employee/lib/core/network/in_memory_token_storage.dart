import 'token_storage.dart';

/// Test-only [TokenStorage]; never used by app code.
class InMemoryTokenStorage implements TokenStorage {
  @override
  String? accessToken;

  String? _refreshToken;

  @override
  Future<String?> readRefreshToken() async => _refreshToken;

  @override
  Future<void> writeRefreshToken(String token) async => _refreshToken = token;

  @override
  Future<void> clear() async {
    accessToken = null;
    _refreshToken = null;
  }
}
