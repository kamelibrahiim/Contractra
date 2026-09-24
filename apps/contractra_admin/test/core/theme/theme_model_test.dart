import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:contractra_admin/core/theme/theme_model.dart';

void main() {
  group('ThemeModel color roles', () {
    test('defaultTheme (light) exposes the full Sprint 1 color role set', () {
      final theme = ThemeModel.defaultTheme;

      expect(theme.isDark, isFalse);
      expect(theme.primaryColor, const Color(0xFF1E5631));
      expect(theme.accentColor, const Color(0xFF8A6D3B));
      expect(theme.backgroundColor, const Color(0xFFF7F5F0));
      expect(theme.surfaceColor, const Color(0xFFFFFFFF));
      expect(theme.borderColor, const Color(0xFFDDD9D0));
      expect(theme.successColor, const Color(0xFF2F7D4F));
      expect(theme.warningColor, const Color(0xFFC98A1E));
      expect(theme.dangerColor, const Color(0xFFB3261E));
      expect(theme.textPrimaryColor, const Color(0xFF23292A));
      expect(theme.textSecondaryColor, const Color(0xFF5B6663));
      expect(theme.disabledColor, const Color(0xFFAEB4B1));
    });

    test('darkTheme exposes the full Sprint 1 color role set', () {
      final theme = ThemeModel.darkTheme;

      expect(theme.isDark, isTrue);
      expect(theme.primaryColor, const Color(0xFF3E8E5B));
      expect(theme.accentColor, const Color(0xFFC7A366));
      expect(theme.backgroundColor, const Color(0xFF171B1A));
      expect(theme.surfaceColor, const Color(0xFF212624));
      expect(theme.borderColor, const Color(0xFF373F3C));
      expect(theme.successColor, const Color(0xFF4CAF72));
      expect(theme.warningColor, const Color(0xFFE0A83F));
      expect(theme.dangerColor, const Color(0xFFE0564D));
      expect(theme.textPrimaryColor, const Color(0xFFEDEFEE));
      expect(theme.textSecondaryColor, const Color(0xFFA9B0AD));
      expect(theme.disabledColor, const Color(0xFF565F5C));
    });

    test('copyWith overrides only the specified color roles', () {
      final base = ThemeModel.defaultTheme;
      final updated = base.copyWith(
        surfaceColor: const Color(0xFF123456),
        dangerColor: const Color(0xFF654321),
      );

      expect(updated.surfaceColor, const Color(0xFF123456));
      expect(updated.dangerColor, const Color(0xFF654321));
      expect(updated.primaryColor, base.primaryColor);
      expect(updated.textPrimaryColor, base.textPrimaryColor);
    });

    test('toJson/fromJson round-trips every color role', () {
      final original = ThemeModel.darkTheme;
      final restored = ThemeModel.fromJson(original.toJson());

      expect(restored.isDark, original.isDark);
      expect(restored.primaryColor, original.primaryColor);
      expect(restored.accentColor, original.accentColor);
      expect(restored.backgroundColor, original.backgroundColor);
      expect(restored.surfaceColor, original.surfaceColor);
      expect(restored.borderColor, original.borderColor);
      expect(restored.successColor, original.successColor);
      expect(restored.warningColor, original.warningColor);
      expect(restored.dangerColor, original.dangerColor);
      expect(restored.textPrimaryColor, original.textPrimaryColor);
      expect(restored.textSecondaryColor, original.textSecondaryColor);
      expect(restored.disabledColor, original.disabledColor);
    });

    test('fromJson loads pre-Sprint-1 JSON (missing new roles) and backfills '
        'them from the matching preset', () {
      // Schema persisted by the app before this task: only isDark,
      // primaryColor, accentColor, backGroundColor, darkGreyColor,
      // lightGreyColor and warningColor existed; darkGreyColor/
      // lightGreyColor have since been removed from ThemeModel entirely.
      final oldLightJson = <String, dynamic>{
        'isDark': false,
        'primaryColor': const Color(0xFF204060).toARGB32(),
        'accentColor': const Color(0xFF102030).toARGB32(),
        'backGroundColor': const Color(0xFFEEEEEE).toARGB32(),
        'darkGreyColor': const Color(0xFF555555).toARGB32(),
        'lightGreyColor': const Color(0xFFAAAAAA).toARGB32(),
        'warningColor': const Color(0xFFFF0000).toARGB32(),
      };

      final restored = ThemeModel.fromJson(oldLightJson);

      // Old, still-present roles are preserved as persisted.
      expect(restored.isDark, isFalse);
      expect(restored.primaryColor, const Color(0xFF204060));
      expect(restored.accentColor, const Color(0xFF102030));
      expect(restored.backgroundColor, const Color(0xFFEEEEEE));
      expect(restored.warningColor, const Color(0xFFFF0000));

      // New roles are missing from the old JSON, so they fall back to
      // defaultTheme (isDark == false).
      expect(restored.surfaceColor, ThemeModel.defaultTheme.surfaceColor);
      expect(restored.borderColor, ThemeModel.defaultTheme.borderColor);
      expect(restored.successColor, ThemeModel.defaultTheme.successColor);
      expect(restored.dangerColor, ThemeModel.defaultTheme.dangerColor);
      expect(
        restored.textPrimaryColor,
        ThemeModel.defaultTheme.textPrimaryColor,
      );
      expect(
        restored.textSecondaryColor,
        ThemeModel.defaultTheme.textSecondaryColor,
      );
      expect(restored.disabledColor, ThemeModel.defaultTheme.disabledColor);
    });

    test('fromJson backfills missing new roles from darkTheme when the old '
        'JSON has isDark == true', () {
      final oldDarkJson = <String, dynamic>{
        'isDark': true,
        'primaryColor': const Color(0xFF204060).toARGB32(),
        'accentColor': const Color(0xFF102030).toARGB32(),
        'backGroundColor': const Color(0xFF000000).toARGB32(),
        'darkGreyColor': const Color(0xFF555555).toARGB32(),
        'lightGreyColor': const Color(0xFFAAAAAA).toARGB32(),
        'warningColor': const Color(0xFFFF0000).toARGB32(),
      };

      final restored = ThemeModel.fromJson(oldDarkJson);

      expect(restored.isDark, isTrue);
      expect(restored.surfaceColor, ThemeModel.darkTheme.surfaceColor);
      expect(restored.textPrimaryColor, ThemeModel.darkTheme.textPrimaryColor);
      expect(restored.disabledColor, ThemeModel.darkTheme.disabledColor);
    });

    test('fromJson still throws on a malformed present value', () {
      final corruptJson = <String, dynamic>{
        'isDark': false,
        'primaryColor': const Color(0xFF204060).toARGB32(),
        'accentColor': const Color(0xFF102030).toARGB32(),
        'backGroundColor': const Color(0xFFEEEEEE).toARGB32(),
        'warningColor': const Color(0xFFFF0000).toARGB32(),
        'surfaceColor': 'not-a-color',
      };

      expect(() => ThemeModel.fromJson(corruptJson), throwsA(anything));
    });

    test('fromJson throws when a new role key is present but null', () {
      final nullValueJson = <String, dynamic>{
        'isDark': false,
        'primaryColor': const Color(0xFF204060).toARGB32(),
        'accentColor': const Color(0xFF102030).toARGB32(),
        'backGroundColor': const Color(0xFFEEEEEE).toARGB32(),
        'warningColor': const Color(0xFFFF0000).toARGB32(),
        'surfaceColor': null,
      };

      expect(() => ThemeModel.fromJson(nullValueJson), throwsA(anything));
    });

    test('toJson serializes only the canonical "backgroundColor" key', () {
      final json = ThemeModel.defaultTheme.toJson();

      expect(
        json['backgroundColor'],
        ThemeModel.defaultTheme.backgroundColor.toARGB32(),
      );
      expect(json.containsKey('backGroundColor'), isFalse);
    });

    test('fromJson reads legacy "backGroundColor" data when the canonical '
        'key is absent', () {
      final legacyJson = <String, dynamic>{
        'isDark': false,
        'primaryColor': const Color(0xFF204060).toARGB32(),
        'accentColor': const Color(0xFF102030).toARGB32(),
        'backGroundColor': const Color(0xFF00FF00).toARGB32(),
        'warningColor': const Color(0xFFFF0000).toARGB32(),
      };

      final restored = ThemeModel.fromJson(legacyJson);

      expect(restored.backgroundColor, const Color(0xFF00FF00));
    });

    test('fromJson prefers canonical "backgroundColor" over legacy '
        '"backGroundColor" when both are present', () {
      final bothKeysJson = <String, dynamic>{
        'isDark': false,
        'primaryColor': const Color(0xFF204060).toARGB32(),
        'accentColor': const Color(0xFF102030).toARGB32(),
        'backgroundColor': const Color(0xFF00FF00).toARGB32(),
        'backGroundColor': const Color(0xFFFF00FF).toARGB32(),
        'warningColor': const Color(0xFFFF0000).toARGB32(),
      };

      final restored = ThemeModel.fromJson(bothKeysJson);

      expect(restored.backgroundColor, const Color(0xFF00FF00));
    });

    test('lerp at t=0 returns this and at t=1 returns other for new roles', () {
      final light = ThemeModel.defaultTheme;
      final dark = ThemeModel.darkTheme;

      final atStart = light.lerp(dark, 0);
      final atEnd = light.lerp(dark, 1);

      expect(atStart.surfaceColor, light.surfaceColor);
      expect(atEnd.surfaceColor, dark.surfaceColor);
      expect(atEnd.textPrimaryColor, dark.textPrimaryColor);
    });
  });
}
