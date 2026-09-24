import 'dart:async';
import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

/// String-key translation lookup, backed by JSON files under i18n/.
/// No screen strings are seeded here yet — that is feature work.
extension Translate on String {
  String get tr => AppLocalizations.instance.translate(this) ?? this;
}

class AppLocalizations {
  final Locale locale;

  AppLocalizations(this.locale);

  static AppLocalizations? of(BuildContext context) =>
      Localizations.of<AppLocalizations>(context, AppLocalizations);

  static late AppLocalizations instance;

  Map<String, String> _localizedStrings = {};

  Future<bool> load() async {
    final jsonString =
        await rootBundle.loadString('i18n/${locale.languageCode}.json');
    final Map<String, dynamic> jsonMap = json.decode(jsonString);
    _localizedStrings =
        jsonMap.map((key, value) => MapEntry(key, value.toString()));
    instance = this;
    return true;
  }

  String? translate(String key) => _localizedStrings[key];
}

class AppLocalizationsDelegate extends LocalizationsDelegate<AppLocalizations> {
  const AppLocalizationsDelegate();

  @override
  bool isSupported(Locale locale) => ['en', 'ar'].contains(locale.languageCode);

  @override
  Future<AppLocalizations> load(Locale locale) async {
    final localizations = AppLocalizations(locale);
    await localizations.load();
    return localizations;
  }

  @override
  bool shouldReload(AppLocalizationsDelegate old) => false;
}
