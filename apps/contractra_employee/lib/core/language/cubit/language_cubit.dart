import 'package:bloc/bloc.dart';

import '../../../utilities/enum.dart';
import '../../../utilities/shared_preferences.dart';

part 'language_state.dart';

class LanguageCubit extends Cubit<LanguageState> {
  LanguageCubit() : super(const LanguageState(appLang: Languages.ar)) {
    fetchLocale();
  }

  void fetchLocale() {
    final saved = SharedPref.getLanguage();
    final lang = Languages.values.firstWhere(
      (l) => l.name == saved,
      orElse: () => Languages.ar,
    );
    emit(state.copyWith(appLang: lang));
  }

  Future<void> changeLanguage({required Languages language}) async {
    if (language == state.appLang) return;
    await SharedPref.setLanguage(lang: language.name);
    emit(state.copyWith(appLang: language));
  }
}
