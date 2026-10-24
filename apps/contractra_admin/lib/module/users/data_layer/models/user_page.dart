import 'user_summary.dart';

/// Paged result for `GET /api/users`.
class UserPage {
  const UserPage({required this.items, required this.totalCount});

  final List<UserSummary> items;
  final int totalCount;

  factory UserPage.fromJson(Map<String, dynamic> json) => UserPage(
        items: (json['items'] as List)
            .map((e) => UserSummary.fromJson(e as Map<String, dynamic>))
            .toList(),
        totalCount: json['totalCount'] as int,
      );
}
