/// Lifecycle values from `parties.status` (Active / NeedsClassification / Inactive).
enum PartyStatus { active, needsClassification, inactive }

/// Read-only list shape of a party: `parties` + its current `party_roles`.
class PartySummary {
  const PartySummary({
    required this.id,
    required this.displayName,
    required this.roleCodes,
    required this.status,
  });

  final String id;
  final String displayName;

  /// Role codes are open strings, matching the backend's `PartyRoleCode`.
  /// A party may have none (e.g. awaiting classification).
  final List<String> roleCodes;
  final PartyStatus status;
}
