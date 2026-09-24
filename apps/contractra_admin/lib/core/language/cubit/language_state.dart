part of 'language_cubit.dart';

class LanguageState {
  final Languages appLang;

  const LanguageState({required this.appLang});

  LanguageState copyWith({Languages? appLang}) =>
      LanguageState(appLang: appLang ?? this.appLang);
}
