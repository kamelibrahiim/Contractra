part of 'font_cubit.dart';

class FontState {
  final FontFamilyTypes fontFamily;
  final double fontSizeScale;

  const FontState({required this.fontFamily, this.fontSizeScale = 1.0});

  FontState copyWith({FontFamilyTypes? fontFamily, double? fontSizeScale}) =>
      FontState(
        fontFamily: fontFamily ?? this.fontFamily,
        fontSizeScale: fontSizeScale ?? this.fontSizeScale,
      );
}
