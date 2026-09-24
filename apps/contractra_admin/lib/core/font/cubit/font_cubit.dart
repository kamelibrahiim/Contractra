import 'package:bloc/bloc.dart';

import '../../../utilities/enum.dart';

part 'font_state.dart';

class FontCubit extends Cubit<FontState> {
  FontCubit() : super(const FontState(fontFamily: FontFamilyTypes.cairo));

  void changeFontFamily(FontFamilyTypes family) =>
      emit(state.copyWith(fontFamily: family));

  void changeFontSizeScale(double scale) =>
      emit(state.copyWith(fontSizeScale: scale));
}
