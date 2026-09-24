part of 'theme_cubit.dart';

class ThemeState {
  final ThemeModel appTheme;

  const ThemeState({required this.appTheme});

  ThemeState copyWith({ThemeModel? appTheme}) =>
      ThemeState(appTheme: appTheme ?? this.appTheme);
}
