/// Shape of `POST /api/auth/login`'s success response.
class LoginResult {
  const LoginResult({
    required this.accessToken,
    required this.refreshToken,
    required this.mustChangePassword,
  });

  final String accessToken;
  final String refreshToken;
  final bool mustChangePassword;

  factory LoginResult.fromJson(Map<String, dynamic> json) => LoginResult(
        accessToken: json['accessToken'] as String,
        refreshToken: json['refreshToken'] as String,
        mustChangePassword: json['mustChangePassword'] as bool,
      );
}
