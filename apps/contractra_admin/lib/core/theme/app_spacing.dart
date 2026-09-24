import 'package:flutter/material.dart';
import 'package:flutter_screenutil_plus/flutter_screenutil_plus.dart';

/// Responsive spacing scale for Contractra Admin. Values only — no widgets,
/// context, or theming logic.
abstract final class AppSpacing {
  /// Very tight internal gaps, e.g. icon-to-text padding.
  static double get xs => 4.w;

  /// Spacing between related controls, e.g. a label and its input.
  static double get sm => 8.w;

  /// Standard component/content spacing.
  static double get md => 16.w;

  /// Section spacing, e.g. between grouped form fields or cards.
  static double get lg => 24.w;

  /// Major layout separation, e.g. between page-level regions.
  static double get xl => 32.w;

  static EdgeInsets get allXs => EdgeInsets.all(xs);
  static EdgeInsets get allSm => EdgeInsets.all(sm);
  static EdgeInsets get allMd => EdgeInsets.all(md);
  static EdgeInsets get allLg => EdgeInsets.all(lg);
  static EdgeInsets get allXl => EdgeInsets.all(xl);

  static EdgeInsets get horizontalXs => EdgeInsets.symmetric(horizontal: xs);
  static EdgeInsets get horizontalSm => EdgeInsets.symmetric(horizontal: sm);
  static EdgeInsets get horizontalMd => EdgeInsets.symmetric(horizontal: md);
  static EdgeInsets get horizontalLg => EdgeInsets.symmetric(horizontal: lg);
  static EdgeInsets get horizontalXl => EdgeInsets.symmetric(horizontal: xl);

  static EdgeInsets get verticalXs => EdgeInsets.symmetric(vertical: xs);
  static EdgeInsets get verticalSm => EdgeInsets.symmetric(vertical: sm);
  static EdgeInsets get verticalMd => EdgeInsets.symmetric(vertical: md);
  static EdgeInsets get verticalLg => EdgeInsets.symmetric(vertical: lg);
  static EdgeInsets get verticalXl => EdgeInsets.symmetric(vertical: xl);
}
