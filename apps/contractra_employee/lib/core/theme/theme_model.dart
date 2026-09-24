import 'package:flutter/material.dart';

/// Design-system container for the Contractra Employee app's color roles.
///
/// Palette derived from the Contractra Employee Stitch visual direction:
/// a lighter, field-friendly tuning of the shared Contractra brand
/// (deep agricultural green, slate neutrals, off-white surfaces, restrained
/// sand/gold accents) optimized for on-site clarity and large touch targets.
class ThemeModel extends ThemeExtension<ThemeModel> {
  static ThemeModel defaultTheme = ThemeModel(
    isDark: false,
    primaryColor: const Color(0xff2F6E4E),
    accentColor: const Color(0xffC98A3A),
    backgroundColor: const Color(0xffF7F6F2),
    surfaceColor: const Color(0xffFFFFFF),
    borderColor: const Color(0xffE1DED5),
    successColor: const Color(0xff2E7D46),
    warningColor: const Color(0xffC77C1E),
    dangerColor: const Color(0xffB3402E),
    textPrimaryColor: const Color(0xff21261F),
    textSecondaryColor: const Color(0xff5B6660),
    disabledColor: const Color(0xffB7BDB6),
  );

  static ThemeModel darkTheme = ThemeModel(
    isDark: true,
    primaryColor: const Color(0xff4C9270),
    accentColor: const Color(0xffD9A25C),
    backgroundColor: const Color(0xff14170F),
    surfaceColor: const Color(0xff1E231B),
    borderColor: const Color(0xff333A2F),
    successColor: const Color(0xff4FA868),
    warningColor: const Color(0xffE0A143),
    dangerColor: const Color(0xffD9694F),
    textPrimaryColor: const Color(0xffF2F1EA),
    textSecondaryColor: const Color(0xffA9B0A4),
    disabledColor: const Color(0xff565D53),
  );

  final bool isDark;
  final Color primaryColor;
  final Color accentColor;
  final Color backgroundColor;
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
    required this.backgroundColor,
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
    Color? backgroundColor,
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
      backgroundColor: backgroundColor ?? this.backgroundColor,
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
        'backgroundColor': backgroundColor.toARGB32(),
        'surfaceColor': surfaceColor.toARGB32(),
        'borderColor': borderColor.toARGB32(),
        'successColor': successColor.toARGB32(),
        'warningColor': warningColor.toARGB32(),
        'dangerColor': dangerColor.toARGB32(),
        'textPrimaryColor': textPrimaryColor.toARGB32(),
        'textSecondaryColor': textSecondaryColor.toARGB32(),
        'disabledColor': disabledColor.toARGB32(),
      };

  factory ThemeModel.fromJson(Map<String, dynamic> json) => ThemeModel(
        isDark: json['isDark'] as bool,
        primaryColor: Color(json['primaryColor'] as int),
        accentColor: Color(json['accentColor'] as int),
        backgroundColor: Color(json['backgroundColor'] as int),
        surfaceColor: Color(json['surfaceColor'] as int),
        borderColor: Color(json['borderColor'] as int),
        successColor: Color(json['successColor'] as int),
        warningColor: Color(json['warningColor'] as int),
        dangerColor: Color(json['dangerColor'] as int),
        textPrimaryColor: Color(json['textPrimaryColor'] as int),
        textSecondaryColor: Color(json['textSecondaryColor'] as int),
        disabledColor: Color(json['disabledColor'] as int),
      );

  @override
  ThemeModel lerp(ThemeExtension<ThemeModel>? other, double t) {
    if (other is! ThemeModel) return this;
    if (identical(this, other)) return this;
    if (t <= 0) return this;
    if (t >= 1) return other;

    return ThemeModel(
      isDark: t < 0.5 ? isDark : other.isDark,
      primaryColor: Color.lerp(primaryColor, other.primaryColor, t)!,
      accentColor: Color.lerp(accentColor, other.accentColor, t)!,
      backgroundColor: Color.lerp(backgroundColor, other.backgroundColor, t)!,
      surfaceColor: Color.lerp(surfaceColor, other.surfaceColor, t)!,
      borderColor: Color.lerp(borderColor, other.borderColor, t)!,
      successColor: Color.lerp(successColor, other.successColor, t)!,
      warningColor: Color.lerp(warningColor, other.warningColor, t)!,
      dangerColor: Color.lerp(dangerColor, other.dangerColor, t)!,
      textPrimaryColor:
          Color.lerp(textPrimaryColor, other.textPrimaryColor, t)!,
      textSecondaryColor:
          Color.lerp(textSecondaryColor, other.textSecondaryColor, t)!,
      disabledColor: Color.lerp(disabledColor, other.disabledColor, t)!,
    );
  }
}
