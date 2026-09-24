import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:contractra_employee/core/theme/theme_model.dart';

void main() {
  group('ThemeModel color roles', () {
    test('defaultTheme exposes the full color role set', () {
      final theme = ThemeModel.defaultTheme;

      expect(theme.isDark, isFalse);
      expect(theme.primaryColor, isA<Color>());
      expect(theme.accentColor, isA<Color>());
      expect(theme.backgroundColor, isA<Color>());
      expect(theme.surfaceColor, isA<Color>());
      expect(theme.borderColor, isA<Color>());
      expect(theme.successColor, isA<Color>());
      expect(theme.warningColor, isA<Color>());
      expect(theme.dangerColor, isA<Color>());
      expect(theme.textPrimaryColor, isA<Color>());
      expect(theme.textSecondaryColor, isA<Color>());
      expect(theme.disabledColor, isA<Color>());
    });

    test('darkTheme is a distinct dark preset with the full role set', () {
      final theme = ThemeModel.darkTheme;

      expect(theme.isDark, isTrue);
      expect(theme.backgroundColor, isNot(ThemeModel.defaultTheme.backgroundColor));
      expect(theme.surfaceColor, isNot(ThemeModel.defaultTheme.surfaceColor));
      expect(theme.textPrimaryColor, isNot(ThemeModel.defaultTheme.textPrimaryColor));
    });

    test('copyWith overrides only the provided color roles', () {
      final base = ThemeModel.defaultTheme;
      const newSuccess = Color(0xff123456);

      final updated = base.copyWith(successColor: newSuccess);

      expect(updated.successColor, newSuccess);
      expect(updated.primaryColor, base.primaryColor);
      expect(updated.accentColor, base.accentColor);
      expect(updated.backgroundColor, base.backgroundColor);
      expect(updated.surfaceColor, base.surfaceColor);
      expect(updated.borderColor, base.borderColor);
      expect(updated.warningColor, base.warningColor);
      expect(updated.dangerColor, base.dangerColor);
      expect(updated.textPrimaryColor, base.textPrimaryColor);
      expect(updated.textSecondaryColor, base.textSecondaryColor);
      expect(updated.disabledColor, base.disabledColor);
    });

    test('toJson/fromJson round-trips every color role', () {
      final theme = ThemeModel.darkTheme;

      final restored = ThemeModel.fromJson(theme.toJson());

      expect(restored.isDark, theme.isDark);
      expect(restored.primaryColor, theme.primaryColor);
      expect(restored.accentColor, theme.accentColor);
      expect(restored.backgroundColor, theme.backgroundColor);
      expect(restored.surfaceColor, theme.surfaceColor);
      expect(restored.borderColor, theme.borderColor);
      expect(restored.successColor, theme.successColor);
      expect(restored.warningColor, theme.warningColor);
      expect(restored.dangerColor, theme.dangerColor);
      expect(restored.textPrimaryColor, theme.textPrimaryColor);
      expect(restored.textSecondaryColor, theme.textSecondaryColor);
      expect(restored.disabledColor, theme.disabledColor);
    });

    test('lerp at t=0 returns this and at t=1 returns other', () {
      final light = ThemeModel.defaultTheme;
      final dark = ThemeModel.darkTheme;

      expect(light.lerp(dark, 0), same(light));
      expect(light.lerp(dark, 1), same(dark));
    });

    test('lerp interpolates color roles at intermediate t', () {
      final light = ThemeModel.defaultTheme;
      final dark = ThemeModel.darkTheme;

      final mid = light.lerp(dark, 0.5);

      expect(
        mid.backgroundColor,
        Color.lerp(light.backgroundColor, dark.backgroundColor, 0.5),
      );
      expect(
        mid.textPrimaryColor,
        Color.lerp(light.textPrimaryColor, dark.textPrimaryColor, 0.5),
      );
    });
  });
}
