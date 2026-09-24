import 'package:flutter/material.dart';
import 'package:flutter_screenutil_plus/flutter_screenutil_plus.dart';

/// Responsive corner-radius scale for Contractra Admin. Values only — no
/// widgets, context, or theming logic.
abstract final class AppRadius {
  /// Compact controls, e.g. chips and small buttons.
  static double get sm => 4.r;

  /// Standard inputs/cards.
  static double get md => 8.r;

  /// Larger containers, e.g. modals and panels.
  static double get lg => 16.r;

  /// Fully rounded buttons/chips.
  static double get pill => 999.r;

  static BorderRadius get smRadius => BorderRadius.circular(sm);
  static BorderRadius get mdRadius => BorderRadius.circular(md);
  static BorderRadius get lgRadius => BorderRadius.circular(lg);
  static BorderRadius get pillRadius => BorderRadius.circular(pill);
}
