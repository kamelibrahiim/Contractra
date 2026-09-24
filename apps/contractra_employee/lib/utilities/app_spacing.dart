import 'package:flutter/material.dart';
import 'package:flutter_screenutil_plus/flutter_screenutil_plus.dart';

/// Spacing scale for the Employee app.
///
/// Values are direction-neutral (no left/right assumptions) so they stay
/// safe under both LTR and RTL layouts; screens choose the logical side via
/// standard [EdgeInsets] APIs. Sizes are resolved through
/// [flutter_screenutil_plus]'s `.w` at call time, so the scale stays
/// responsive across mobile and web without hard-coded breakpoints.
abstract final class AppSpacing {
  static double get xs => 4.w;
  static double get sm => 8.w;
  static double get md => 12.w;
  static double get lg => 16.w;
  static double get xl => 24.w;
  static double get xxl => 32.w;

  static EdgeInsets get allXs => EdgeInsets.all(xs);
  static EdgeInsets get allSm => EdgeInsets.all(sm);
  static EdgeInsets get allMd => EdgeInsets.all(md);
  static EdgeInsets get allLg => EdgeInsets.all(lg);
  static EdgeInsets get allXl => EdgeInsets.all(xl);
  static EdgeInsets get allXxl => EdgeInsets.all(xxl);

  static EdgeInsets get horizontalXs => EdgeInsets.symmetric(horizontal: xs);
  static EdgeInsets get horizontalSm => EdgeInsets.symmetric(horizontal: sm);
  static EdgeInsets get horizontalMd => EdgeInsets.symmetric(horizontal: md);
  static EdgeInsets get horizontalLg => EdgeInsets.symmetric(horizontal: lg);
  static EdgeInsets get horizontalXl => EdgeInsets.symmetric(horizontal: xl);
  static EdgeInsets get horizontalXxl => EdgeInsets.symmetric(horizontal: xxl);

  static EdgeInsets get verticalXs => EdgeInsets.symmetric(vertical: xs);
  static EdgeInsets get verticalSm => EdgeInsets.symmetric(vertical: sm);
  static EdgeInsets get verticalMd => EdgeInsets.symmetric(vertical: md);
  static EdgeInsets get verticalLg => EdgeInsets.symmetric(vertical: lg);
  static EdgeInsets get verticalXl => EdgeInsets.symmetric(vertical: xl);
  static EdgeInsets get verticalXxl => EdgeInsets.symmetric(vertical: xxl);
}
