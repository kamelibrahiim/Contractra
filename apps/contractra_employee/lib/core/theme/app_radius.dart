import 'package:flutter/material.dart';
import 'package:flutter_screenutil_plus/flutter_screenutil_plus.dart';

/// Border-radius scale for the Employee app, kept consistent with the
/// ~8px base roundness of the Stitch visual direction from TASK-001.
///
/// Sizes are resolved through [flutter_screenutil_plus]'s `.r` at call
/// time, so the scale stays responsive across mobile and web.
abstract final class AppRadius {
  static double get sm => 4.r;
  static double get md => 8.r;
  static double get lg => 12.r;
  static double get xl => 16.r;

  /// Large enough to fully round any touch-target-sized control (pills,
  /// chips, circular buttons); callers should pair it with [BoxShape.circle]
  /// or an explicit half-height when the exact radius must match a widget's
  /// own size.
  static double get pill => 999.r;

  static BorderRadius get smRadius => BorderRadius.circular(sm);
  static BorderRadius get mdRadius => BorderRadius.circular(md);
  static BorderRadius get lgRadius => BorderRadius.circular(lg);
  static BorderRadius get xlRadius => BorderRadius.circular(xl);
  static BorderRadius get pillRadius => BorderRadius.circular(pill);
}
