import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../../../core/theme/app_spacing.dart';
import '../../../../core/theme/app_theme.dart';
import '../../../../utilities/text_style_helper.dart';
import '../../../../widget/app_card.dart';
import '../../data_layer/models/user_summary.dart';
import 'user_status_badge.dart';

/// Narrow-width (<600px) row, used instead of the table.
class UserRowCard extends StatelessWidget {
  const UserRowCard({super.key, required this.user, this.onTap});

  final UserSummary user;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final theme = context.watch<ThemeCubit>().state.appTheme;

    return InkWell(
      onTap: onTap,
      child: AppCard(
        padding: AppSpacing.allMd,
        child: Row(
          children: [
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    user.loginIdentifier,
                    style: TextStyleHelper.bodyLg.copyWith(
                      color: theme.textPrimaryColor,
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                  SizedBox(height: AppSpacing.xs),
                  Text(
                    user.roles.join(', '),
                    style: TextStyleHelper.bodySm.copyWith(color: theme.textSecondaryColor),
                  ),
                ],
              ),
            ),
            SizedBox(width: AppSpacing.md),
            UserStatusBadge(status: user.status),
          ],
        ),
      ),
    );
  }
}
