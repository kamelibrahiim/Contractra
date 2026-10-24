import 'package:flutter_secure_storage/flutter_secure_storage.dart';

import 'token_storage.dart';

class SecureTokenStorage implements TokenStorage {
  SecureTokenStorage({FlutterSecureStorage? storage})
      : _storage = storage ?? const FlutterSecureStorage();

  static const _refreshTokenKey = 'contractra.refresh_token';

  final FlutterSecureStorage _storage;

  @override
  String? accessToken;

  @override
  Future<String?> readRefreshToken() => _storage.read(key: _refreshTokenKey);

  @override
  Future<void> writeRefreshToken(String token) =>
      _storage.write(key: _refreshTokenKey, value: token);

  @override
  Future<void> clear() async {
    accessToken = null;
    await _storage.delete(key: _refreshTokenKey);
  }
}
