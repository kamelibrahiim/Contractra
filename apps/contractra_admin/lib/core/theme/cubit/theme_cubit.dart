import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../../utilities/shared_preferences.dart';
import '../theme_model.dart';

part 'theme_state.dart';

class ThemeCubit extends Cubit<ThemeState> {
  ThemeCubit() : super(ThemeState(appTheme: ThemeModel.defaultTheme)) {
    fetchTheme();
  }

  void fetchTheme() {
    emit(state.copyWith(appTheme: SharedPref.getTheme() ?? ThemeModel.defaultTheme));
  }

  ThemeData get appThemeMode =>
      state.appTheme.isDark ? _darkMode : _lightMode;

  ThemeData get _darkMode => ThemeData.dark(useMaterial3: false)
      .copyWith(extensions: <ThemeExtension<ThemeModel>>[state.appTheme]);

  ThemeData get _lightMode => ThemeData.light(useMaterial3: false)
      .copyWith(extensions: <ThemeExtension<ThemeModel>>[state.appTheme]);

  Future<void> changeTheme({required ThemeModel theme}) async {
    await SharedPref.setTheme(theme: theme);
    emit(state.copyWith(appTheme: theme));
  }
}
