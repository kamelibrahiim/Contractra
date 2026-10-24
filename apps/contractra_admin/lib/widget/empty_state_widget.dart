import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../core/theme/app_spacing.dart';
import '../core/theme/app_theme.dart';
import '../utilities/text_style_helper.dart';
import 'app_button.dart';

/// Generic "nothing to show" placeholder.
///
/// Content (title/message/action) is always supplied by the caller — this
/// widget never hardcodes copy for a specific feature, and never fetches
/// data or reads a cubit itself. A feature's own cubit decides when to
/// show this versus [LoadingStateWidget]/[ErrorStateWidget].
class EmptyStateWidget extends StatelessWidget {
  const EmptyStateWidget({
    super.key,
    required this.title,
    this.message,
    this.actionLabel,
    this.onAction,
    this.icon = Icons.inbox_outlined,
  });

  final String title;
  final String? message;
  final String? actionLabel;
  final VoidCallback? onAction;
  final IconData icon;

  @override
  Widget build(BuildContext context) {
    final theme = context.watch<ThemeCubit>().state.appTheme;

    return Center(
      child: Padding(
        padding: AppSpacing.allLg,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(icon, size: 48, color: theme.textSecondaryColor),
            SizedBox(height: AppSpacing.md),
            Text(
              title,
              textAlign: TextAlign.center,
              style: TextStyleHelper.h4.copyWith(color: theme.textPrimaryColor),
            ),
            if (message != null) ...[
              SizedBox(height: AppSpacing.sm),
              Text(
                message!,
                textAlign: TextAlign.center,
                style: TextStyleHelper.bodyMd.copyWith(
                  color: theme.textSecondaryColor,
                ),
              ),
            ],
            if (actionLabel != null && onAction != null) ...[
              SizedBox(height: AppSpacing.lg),
              AppButton(
                label: actionLabel!,
                onPressed: onAction,
                variant: AppButtonVariant.secondary,
              ),
            ],
          ],
        ),
      ),
    );
  }
}
