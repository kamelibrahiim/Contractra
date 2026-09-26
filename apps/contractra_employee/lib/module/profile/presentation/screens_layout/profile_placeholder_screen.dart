import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../../../core/language/locales.dart';
import '../../../../core/session/session_cubit.dart';
import '../../../../core/theme/app_spacing.dart';
import '../../../../widget/app_button.dart';
import '../../../../widget/empty_state_widget.dart';

/// Profile content is still placeholder (blocked on real profile data), but
/// logout is a real action per US-010/TASK-034 — the Employee app's only
/// exit besides the forced change-password screen.
class ProfilePlaceholderScreen extends StatelessWidget {
  const ProfilePlaceholderScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return Column(
      children: [
        Expanded(
          child: EmptyStateWidget(
            icon: Icons.construction_outlined,
            title: 'navProfile'.tr,
            message: 'moduleComingSoon'.tr,
          ),
        ),
        Padding(
          padding: AppSpacing.allLg,
          child: AppButton(
            key: const Key('profile_logout'),
            label: 'logout'.tr,
            variant: AppButtonVariant.secondary,
            isFullWidth: true,
            onPressed: () => context.read<SessionCubit>().logout(),
          ),
        ),
      ],
    );
  }
}
