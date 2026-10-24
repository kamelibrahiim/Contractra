/// Shape of `GET /api/auth/me`. Field names follow
/// claude-code-prompt-02-frontend.md; no published API contract exists yet
/// (frontend-auth-and-users-plan.md, gap G1).
class CurrentUser {
  const CurrentUser({
    required this.id,
    required this.loginIdentifier,
    required this.roles,
    required this.permissions,
    required this.mustChangePassword,
  });

  final String id;
  final String loginIdentifier;
  final List<String> roles;
  final List<String> permissions;
  final bool mustChangePassword;

  bool hasPermission(String permission) => permissions.contains(permission);

  factory CurrentUser.fromJson(Map<String, dynamic> json) => CurrentUser(
        id: json['id'] as String,
        loginIdentifier: json['loginIdentifier'] as String,
        roles: (json['roles'] as List).cast<String>(),
        permissions: (json['permissions'] as List).cast<String>(),
        mustChangePassword: json['mustChangePassword'] as bool,
      );
}
