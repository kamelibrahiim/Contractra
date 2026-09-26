import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../core/theme/app_radius.dart';
import '../core/theme/app_spacing.dart';
import '../core/theme/app_theme.dart';
import '../utilities/text_style_helper.dart';

/// Visual intent of an [AppButton]; the widget stays a single component so
/// callers pick a variant instead of choosing between separate button
/// classes.
enum AppButtonVariant { primary, secondary, text }

/// Contractra Admin's single reusable button.
///
/// Colors come from [ThemeModel] (via [appTheme]), spacing from
/// [AppSpacing], radius from [AppRadius], and typography from
/// [TextStyleHelper] — no palette, spacing, or font values are duplicated
/// here. The button is disabled whenever [onPressed] is null or
/// [isLoading] is true, and swaps its content for a [CircularProgressIndicator]
/// while loading rather than growing/shrinking, so clicks can't double-submit.
class AppButton extends StatelessWidget {
  const AppButton({
    super.key,
    required this.label,
    required this.onPressed,
    this.variant = AppButtonVariant.primary,
    this.isLoading = false,
    this.isFullWidth = false,
    this.leadingIcon,
    this.trailingIcon,
  });

  final String label;
  final VoidCallback? onPressed;
  final AppButtonVariant variant;
  final bool isLoading;
  final bool isFullWidth;
  final IconData? leadingIcon;
  final IconData? trailingIcon;

  static const double _minHeight = 44;

  // Icon/indicator size: not a spacing or radius concern, so it isn't
  // sourced from AppSpacing/AppRadius; kept local and fixed instead of a
  // new token since AppButton is the only place icons are sized this way
  // today.
  static const double _iconSize = 20;

  bool get _isEnabled => onPressed != null && !isLoading;

  @override
  Widget build(BuildContext context) {
    final theme = context.watch<ThemeCubit>().state.appTheme;
    final colors = _AppButtonColors.resolve(theme, variant, _isEnabled);

    final content = isLoading
        ? SizedBox(
            width: _iconSize,
            height: _iconSize,
            child: CircularProgressIndicator(
              strokeWidth: 2.5,
              valueColor: AlwaysStoppedAnimation<Color>(colors.foreground),
            ),
          )
        : Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              if (leadingIcon != null) ...[
                Icon(leadingIcon, size: _iconSize, color: colors.foreground),
                SizedBox(width: AppSpacing.sm),
              ],
              Flexible(
                child: Text(
                  label,
                  textAlign: TextAlign.center,
                  overflow: TextOverflow.ellipsis,
                  style: TextStyleHelper.label.copyWith(color: colors.foreground),
                ),
              ),
              if (trailingIcon != null) ...[
                SizedBox(width: AppSpacing.sm),
                Icon(trailingIcon, size: _iconSize, color: colors.foreground),
              ],
            ],
          );

    return SizedBox(
      width: isFullWidth ? double.infinity : null,
      height: _minHeight,
      child: Material(
        color: colors.background,
        shape: RoundedRectangleBorder(
          borderRadius: AppRadius.mdRadius,
          side: colors.border == null
              ? BorderSide.none
              : BorderSide(color: colors.border!, width: 1.5),
        ),
        child: InkWell(
          onTap: _isEnabled ? onPressed : null,
          borderRadius: AppRadius.mdRadius,
          child: Padding(
            padding: AppSpacing.horizontalLg,
            child: content,
          ),
        ),
      ),
    );
  }
}

class _AppButtonColors {
  const _AppButtonColors({
    required this.background,
    required this.foreground,
    this.border,
  });

  final Color background;
  final Color foreground;
  final Color? border;

  factory _AppButtonColors.resolve(
    ThemeModel theme,
    AppButtonVariant variant,
    bool isEnabled,
  ) {
    if (!isEnabled) {
      return _AppButtonColors(
        background: variant == AppButtonVariant.primary
            ? theme.disabledColor
            : Colors.transparent,
        foreground: theme.disabledColor,
        border: variant == AppButtonVariant.secondary ? theme.disabledColor : null,
      );
    }

    switch (variant) {
      case AppButtonVariant.primary:
        return _AppButtonColors(
          background: theme.primaryColor,
          foreground: theme.surfaceColor,
        );
      case AppButtonVariant.secondary:
        return _AppButtonColors(
          background: Colors.transparent,
          foreground: theme.primaryColor,
          border: theme.primaryColor,
        );
      case AppButtonVariant.text:
        return _AppButtonColors(
          background: Colors.transparent,
          foreground: theme.primaryColor,
        );
    }
  }
}
