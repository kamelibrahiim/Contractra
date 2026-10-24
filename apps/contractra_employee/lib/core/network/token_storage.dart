/// Holds session tokens. The access token lives in memory only and is
/// never persisted; only the refresh token is written to storage, so a
/// compromised storage read alone cannot mint new access tokens without
/// also completing a refresh call (plan G9 — web storage trade-off: even
/// `flutter_secure_storage`'s web backend keeps its wrapping key in
/// `localStorage`, which is not immune to XSS, so persisting only the
/// refresh token limits the blast radius of that read).
abstract class TokenStorage {
  String? get accessToken;
  set accessToken(String? value);

  Future<String?> readRefreshToken();
  Future<void> writeRefreshToken(String token);
  Future<void> clear();
}
