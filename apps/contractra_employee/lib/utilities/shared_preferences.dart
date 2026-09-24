import 'dart:convert';

import 'package:get_it/get_it.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../core/theme/theme_model.dart';

/// Thin wrapper over [SharedPreferences] for the few structural, non-business
/// settings the app shell needs (theme + language). Feature-level storage
/// belongs to that feature's own repository, not here.
class SharedPref {
  SharedPref._();

  static SharedPreferences get _prefs => GetIt.I<SharedPreferences>();

  static const _themeKey = 'app_theme';
  static const _languageKey = 'app_language';

  static Future<void> setTheme({required ThemeModel theme}) async {
    await _prefs.setString(_themeKey, jsonEncode(theme.toJson()));
  }

  static ThemeModel? getTheme() {
    final raw = _prefs.getString(_themeKey);
    if (raw == null) return null;
    return ThemeModel.fromJson(jsonDecode(raw) as Map<String, dynamic>);
  }

  static Future<void> setLanguage({required String lang}) async {
    await _prefs.setString(_languageKey, lang);
  }

  static String? getLanguage() => _prefs.getString(_languageKey);
}
