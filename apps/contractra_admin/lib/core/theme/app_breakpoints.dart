/// Layout width breakpoints in logical pixels. Compared against raw
/// `MediaQuery`/`LayoutBuilder` widths, so they are intentionally unscaled.
abstract final class AppBreakpoints {
  /// At or above this width the side navigation rail is always visible.
  static const double large = 1200;

  /// At or above this width the layout is treated as tablet-sized.
  static const double medium = 600;
}
