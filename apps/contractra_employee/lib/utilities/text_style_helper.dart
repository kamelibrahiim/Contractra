import 'package:flutter/material.dart';
import 'package:flutter_screenutil_plus/flutter_screenutil_plus.dart';
import 'package:google_fonts/google_fonts.dart';

/// Typography scale for the Employee app.
///
/// Built on Cairo (the app's default font family, see [FontCubit]) because it
/// renders Arabic and Latin scripts with matched, professional-weight glyphs
/// — this app is Arabic-first, so the scale must not rely on a Latin-only
/// display font that would silently fall back for Arabic text.
///
/// Sizes are defined in design-space px and resolved through
/// [flutter_screenutil_plus]'s `.sp` at call time, so every role stays
/// responsive across mobile and web without hard-coded breakpoints.
abstract final class TextStyleHelper {
  static TextStyle get h1 => _style(fontSize: 32, fontWeight: FontWeight.w700);

  static TextStyle get h2 => _style(fontSize: 26, fontWeight: FontWeight.w700);

  static TextStyle get h3 => _style(fontSize: 21, fontWeight: FontWeight.w600);

  static TextStyle get h4 => _style(fontSize: 18, fontWeight: FontWeight.w600);

  static TextStyle get bodyLg =>
      _style(fontSize: 16, fontWeight: FontWeight.w500);

  static TextStyle get bodyMd =>
      _style(fontSize: 14, fontWeight: FontWeight.w400);

  static TextStyle get bodySm =>
      _style(fontSize: 12, fontWeight: FontWeight.w400);

  static TextStyle get caption =>
      _style(fontSize: 11, fontWeight: FontWeight.w400);

  static TextStyle get label =>
      _style(fontSize: 13, fontWeight: FontWeight.w600);

  static TextStyle _style({
    required double fontSize,
    required FontWeight fontWeight,
  }) =>
      GoogleFonts.cairo(fontSize: fontSize.sp, fontWeight: fontWeight);
}
