import 'package:flutter_screenutil_plus/flutter_screenutil_plus.dart';

/// Responsive spacing scale for Contractra Admin. Values only — no widgets,
/// context, or theming logic.
class AppSpacing {
  const AppSpacing._();

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
}

/// Responsive corner-radius scale for Contractra Admin. Values only — no
/// widgets, context, or theming logic.
class AppRadius {
  const AppRadius._();

  /// Compact controls, e.g. chips and small buttons.
  static double get sm => 4.r;

  /// Standard inputs/cards.
  static double get md => 8.r;

  /// Larger containers, e.g. modals and panels.
  static double get lg => 16.r;

  /// Fully rounded buttons/chips.
  static double get pill => 999.r;
}
