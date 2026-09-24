import 'package:flutter/material.dart';

/// Structural design-system container. Sprint 1 fills in the full Contractra
/// Admin color role set here; typography/spacing tokens are added in later
/// Sprint 1 tasks.
class ThemeModel extends ThemeExtension<ThemeModel> {
  static final ThemeModel defaultTheme = ThemeModel(
    isDark: false,
    primaryColor: const Color(0xFF1E5631),
    accentColor: const Color(0xFF8A6D3B),
    backGroundColor: const Color(0xFFF7F5F0),
    surfaceColor: const Color(0xFFFFFFFF),
    borderColor: const Color(0xFFDDD9D0),
    successColor: const Color(0xFF2F7D4F),
    warningColor: const Color(0xFFC98A1E),
    dangerColor: const Color(0xFFB3261E),
    textPrimaryColor: const Color(0xFF23292A),
    textSecondaryColor: const Color(0xFF5B6663),
    disabledColor: const Color(0xFFAEB4B1),
  );

  static final ThemeModel darkTheme = ThemeModel(
    isDark: true,
    primaryColor: const Color(0xFF3E8E5B),
    accentColor: const Color(0xFFC7A366),
    backGroundColor: const Color(0xFF171B1A),
    surfaceColor: const Color(0xFF212624),
    borderColor: const Color(0xFF373F3C),
    successColor: const Color(0xFF4CAF72),
    warningColor: const Color(0xFFE0A83F),
    dangerColor: const Color(0xFFE0564D),
    textPrimaryColor: const Color(0xFFEDEFEE),
    textSecondaryColor: const Color(0xFFA9B0AD),
    disabledColor: const Color(0xFF565F5C),
  );

  final bool isDark;
  final Color primaryColor;
  final Color accentColor;
  final Color backGroundColor;
  final Color surfaceColor;
  final Color borderColor;
  final Color successColor;
  final Color warningColor;
  final Color dangerColor;
  final Color textPrimaryColor;
  final Color textSecondaryColor;
  final Color disabledColor;

  ThemeModel({
    this.isDark = false,
    required this.primaryColor,
    required this.accentColor,
    required this.backGroundColor,
    required this.surfaceColor,
    required this.borderColor,
    required this.successColor,
    required this.warningColor,
    required this.dangerColor,
    required this.textPrimaryColor,
    required this.textSecondaryColor,
    required this.disabledColor,
  });

  @override
  ThemeModel copyWith({
    bool? isDark,
    Color? primaryColor,
    Color? accentColor,
    Color? backGroundColor,
    Color? surfaceColor,
    Color? borderColor,
    Color? successColor,
    Color? warningColor,
    Color? dangerColor,
    Color? textPrimaryColor,
    Color? textSecondaryColor,
    Color? disabledColor,
  }) {
    return ThemeModel(
      isDark: isDark ?? this.isDark,
      primaryColor: primaryColor ?? this.primaryColor,
      accentColor: accentColor ?? this.accentColor,
      backGroundColor: backGroundColor ?? this.backGroundColor,
      surfaceColor: surfaceColor ?? this.surfaceColor,
      borderColor: borderColor ?? this.borderColor,
      successColor: successColor ?? this.successColor,
      warningColor: warningColor ?? this.warningColor,
      dangerColor: dangerColor ?? this.dangerColor,
      textPrimaryColor: textPrimaryColor ?? this.textPrimaryColor,
      textSecondaryColor: textSecondaryColor ?? this.textSecondaryColor,
      disabledColor: disabledColor ?? this.disabledColor,
    );
  }

  Map<String, dynamic> toJson() => {
    'isDark': isDark,
    'primaryColor': primaryColor.toARGB32(),
    'accentColor': accentColor.toARGB32(),
    'backGroundColor': backGroundColor.toARGB32(),
    'surfaceColor': surfaceColor.toARGB32(),
    'borderColor': borderColor.toARGB32(),
    'successColor': successColor.toARGB32(),
    'warningColor': warningColor.toARGB32(),
    'dangerColor': dangerColor.toARGB32(),
    'textPrimaryColor': textPrimaryColor.toARGB32(),
    'textSecondaryColor': textSecondaryColor.toARGB32(),
    'disabledColor': disabledColor.toARGB32(),
  };

  /// Builds a [ThemeModel] from persisted JSON. Older app versions persisted
  /// a smaller color-role set (no [surfaceColor], [borderColor],
  /// [successColor], [dangerColor], [textPrimaryColor], [textSecondaryColor],
  /// [disabledColor]), so those roles fall back to the matching preset
  /// (`defaultTheme`/`darkTheme`, chosen by the persisted `isDark`) when
  /// absent from the JSON (key not present at all). A key that *is* present
  /// — including a present `null` — but malformed still throws, since that
  /// indicates genuinely corrupted data rather than an old schema.
  factory ThemeModel.fromJson(Map<String, dynamic> json) {
    final isDark = json['isDark'] as bool;
    final fallback = isDark ? darkTheme : defaultTheme;

    Color colorOr(String key, Color fallbackColor) {
      if (!json.containsKey(key)) return fallbackColor;
      return Color(json[key] as int);
    }

    return ThemeModel(
      isDark: isDark,
      primaryColor: colorOr('primaryColor', fallback.primaryColor),
      accentColor: colorOr('accentColor', fallback.accentColor),
      backGroundColor: colorOr('backGroundColor', fallback.backGroundColor),
      surfaceColor: colorOr('surfaceColor', fallback.surfaceColor),
      borderColor: colorOr('borderColor', fallback.borderColor),
      successColor: colorOr('successColor', fallback.successColor),
      warningColor: colorOr('warningColor', fallback.warningColor),
      dangerColor: colorOr('dangerColor', fallback.dangerColor),
      textPrimaryColor: colorOr('textPrimaryColor', fallback.textPrimaryColor),
      textSecondaryColor: colorOr(
        'textSecondaryColor',
        fallback.textSecondaryColor,
      ),
      disabledColor: colorOr('disabledColor', fallback.disabledColor),
    );
  }

  @override
  ThemeModel lerp(ThemeExtension<ThemeModel>? other, double t) {
    if (other is! ThemeModel) return this;
    return ThemeModel(
      isDark: t < 0.5 ? isDark : other.isDark,
      primaryColor: Color.lerp(primaryColor, other.primaryColor, t)!,
      accentColor: Color.lerp(accentColor, other.accentColor, t)!,
      backGroundColor: Color.lerp(backGroundColor, other.backGroundColor, t)!,
      surfaceColor: Color.lerp(surfaceColor, other.surfaceColor, t)!,
      borderColor: Color.lerp(borderColor, other.borderColor, t)!,
      successColor: Color.lerp(successColor, other.successColor, t)!,
      warningColor: Color.lerp(warningColor, other.warningColor, t)!,
      dangerColor: Color.lerp(dangerColor, other.dangerColor, t)!,
      textPrimaryColor: Color.lerp(
        textPrimaryColor,
        other.textPrimaryColor,
        t,
      )!,
      textSecondaryColor: Color.lerp(
        textSecondaryColor,
        other.textSecondaryColor,
        t,
      )!,
      disabledColor: Color.lerp(disabledColor, other.disabledColor, t)!,
    );
  }
}
