import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../../../core/language/locales.dart';
import '../../../../core/theme/app_radius.dart';
import '../../../../core/theme/app_spacing.dart';
import '../../../../core/theme/app_theme.dart';
import '../../../../utilities/text_style_helper.dart';
import '../../data_layer/models/user_summary.dart';

class UserStatusBadge extends StatelessWidget {
  const UserStatusBadge({super.key, required this.status});

  final UserAccountStatus status;

  @override
  Widget build(BuildContext context) {
    final theme = context.watch<ThemeCubit>().state.appTheme;
    final isActive = status == UserAccountStatus.active;
    final color = isActive ? theme.successColor : theme.disabledColor;

    return DecoratedBox(
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.12),
        borderRadius: AppRadius.pillRadius,
      ),
      child: Padding(
        padding: EdgeInsets.symmetric(horizontal: AppSpacing.sm, vertical: 2),
        child: Text(
          isActive ? 'userStatusActive'.tr : 'userStatusDisabled'.tr,
          style: TextStyleHelper.caption.copyWith(color: color, fontWeight: FontWeight.w600),
        ),
      ),
    );
  }
}
