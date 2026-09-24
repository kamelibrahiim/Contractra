import 'package:flutter/material.dart';
import 'package:flutter_screenutil_plus/flutter_screenutil_plus.dart';
import 'package:google_fonts/google_fonts.dart';

/// Semantic typography scale for Contractra Admin.
///
/// Cairo is used as the base font because it is already the app's default
/// font family (see [FontCubit]) and reads well for both Arabic and Latin
/// text, which this Admin app needs as an Arabic-first, RTL-compatible,
/// professional admin/accounting tool. Sizes use `.sp` for responsive
/// scaling. Colors are intentionally left unset — callers apply the
/// appropriate `ThemeModel` color role.
class TextStyleHelper {
  const TextStyleHelper._();

  static TextStyleHelper of(BuildContext context) => const TextStyleHelper._();

  TextStyle get h1 => GoogleFonts.cairo(
    fontSize: 32.sp,
    fontWeight: FontWeight.w700,
    height: 1.25,
  );

  TextStyle get h2 => GoogleFonts.cairo(
    fontSize: 26.sp,
    fontWeight: FontWeight.w700,
    height: 1.28,
  );

  TextStyle get h3 => GoogleFonts.cairo(
    fontSize: 22.sp,
    fontWeight: FontWeight.w600,
    height: 1.3,
  );

  TextStyle get h4 => GoogleFonts.cairo(
    fontSize: 18.sp,
    fontWeight: FontWeight.w600,
    height: 1.35,
  );

  TextStyle get bodyLg => GoogleFonts.cairo(
    fontSize: 16.sp,
    fontWeight: FontWeight.w400,
    height: 1.5,
  );

  TextStyle get bodyMd => GoogleFonts.cairo(
    fontSize: 14.sp,
    fontWeight: FontWeight.w400,
    height: 1.5,
  );

  TextStyle get bodySm => GoogleFonts.cairo(
    fontSize: 12.sp,
    fontWeight: FontWeight.w400,
    height: 1.45,
  );

  TextStyle get caption => GoogleFonts.cairo(
    fontSize: 11.sp,
    fontWeight: FontWeight.w400,
    height: 1.4,
  );

  TextStyle get label => GoogleFonts.cairo(
    fontSize: 13.sp,
    fontWeight: FontWeight.w600,
    height: 1.3,
  );
}
