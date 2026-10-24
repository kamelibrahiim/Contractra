enum UserAccountStatus { active, disabled }

/// Row shape for `GET /api/users`.
class UserSummary {
  const UserSummary({
    required this.id,
    required this.loginIdentifier,
    required this.roles,
    required this.status,
  });

  final String id;
  final String loginIdentifier;
  final List<String> roles;
  final UserAccountStatus status;

  factory UserSummary.fromJson(Map<String, dynamic> json) => UserSummary(
        id: json['id'] as String,
        loginIdentifier: json['loginIdentifier'] as String,
        roles: (json['roles'] as List).cast<String>(),
        status: json['status'] == 'Disabled' ? UserAccountStatus.disabled : UserAccountStatus.active,
      );
}
