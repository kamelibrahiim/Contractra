import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../core/theme/app_radius.dart';
import '../core/theme/app_spacing.dart';
import '../core/theme/app_theme.dart';
import '../utilities/text_style_helper.dart';

/// One navigation entry in the Admin shell. The caller decides [isActive]
/// from the current route; this widget only renders that state.
class NavRailItem extends StatelessWidget {
  const NavRailItem({
    super.key,
    required this.icon,
    required this.label,
    required this.isActive,
    required this.onTap,
  });

  final IconData icon;
  final String label;
  final bool isActive;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final theme = context.watch<ThemeCubit>().state.appTheme;
    final color = isActive ? theme.primaryColor : theme.textSecondaryColor;

    return Semantics(
      selected: isActive,
      button: true,
      child: Material(
        color: isActive ? theme.primaryColor.withValues(alpha: 0.12) : Colors.transparent,
        borderRadius: AppRadius.mdRadius,
        child: InkWell(
          borderRadius: AppRadius.mdRadius,
          onTap: onTap,
          child: Padding(
            padding: EdgeInsets.symmetric(horizontal: AppSpacing.md, vertical: AppSpacing.sm),
            child: Row(
              children: [
                Icon(icon, color: color, size: 22),
                SizedBox(width: AppSpacing.sm),
                Expanded(
                  child: Text(
                    label,
                    overflow: TextOverflow.ellipsis,
                    style: TextStyleHelper.bodyMd.copyWith(
                      color: color,
                      fontWeight: isActive ? FontWeight.w600 : FontWeight.w400,
                    ),
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
