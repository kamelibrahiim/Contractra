/// Row shape for `GET /api/roles`.
class RoleOption {
  const RoleOption({required this.code, required this.displayName});

  final String code;
  final String displayName;

  factory RoleOption.fromJson(Map<String, dynamic> json) => RoleOption(
        code: json['code'] as String,
        displayName: json['displayName'] as String,
      );
}
