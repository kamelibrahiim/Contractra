import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../core/theme/app_radius.dart';
import '../core/theme/app_spacing.dart';
import '../core/theme/app_theme.dart';

/// Themed container used to group content across screens.
///
/// Colors come from [ThemeModel], radius from [AppRadius], and padding
/// from [AppSpacing] — no content assumptions live here, only layout and
/// elevation. [padding] defaults to [AppSpacing.allMd] and can be
/// overridden per call site.
class AppCard extends StatelessWidget {
  const AppCard({
    super.key,
    required this.child,
    this.padding,
    this.elevation = 0,
  });

  final Widget child;
  final EdgeInsetsGeometry? padding;
  final double elevation;

  @override
  Widget build(BuildContext context) {
    final theme = context.watch<ThemeCubit>().state.appTheme;

    return Material(
      color: theme.surfaceColor,
      elevation: elevation,
      shadowColor: theme.borderColor,
      shape: RoundedRectangleBorder(
        borderRadius: AppRadius.mdRadius,
        side: BorderSide(color: theme.borderColor),
      ),
      child: Padding(
        padding: padding ?? AppSpacing.allMd,
        child: child,
      ),
    );
  }
}
