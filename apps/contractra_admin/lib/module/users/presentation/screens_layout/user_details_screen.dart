import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../../../core/language/locales.dart';
import '../../../../core/theme/app_spacing.dart';
import '../../../../core/theme/app_theme.dart';
import '../../../../utilities/git_it.dart';
import '../../../../utilities/text_style_helper.dart';
import '../../../../widget/app_button.dart';
import '../../../../widget/error_state_widget.dart';
import '../../../../widget/loading_state_widget.dart';
import '../../cubit/user_actions_cubit.dart';
import '../../data_layer/models/user_summary.dart';
import '../../data_layer/repository/users_repository.dart';
import '../widgets/confirm_dialog.dart';
import '../widgets/default_password_dialog.dart';
import '../widgets/user_status_badge.dart';

class UserDetailsScreen extends StatelessWidget {
  const UserDetailsScreen({super.key, required this.userId});

  final String userId;

  @override
  Widget build(BuildContext context) {
    return BlocProvider(
      create: (_) => UserActionsCubit(GitIt.instance<UsersRepository>(), userId),
      child: const _UserDetailsView(),
    );
  }
}

class _UserDetailsView extends StatefulWidget {
  const _UserDetailsView();

  @override
  State<_UserDetailsView> createState() => _UserDetailsViewState();
}

class _UserDetailsViewState extends State<_UserDetailsView> {
  Future<void> _disable(BuildContext context, UserActionsCubit cubit) async {
    final confirmed = await showConfirmDialog(
      context,
      title: 'usersConfirmDisableTitle'.tr,
      message: 'usersConfirmDisableMessage'.tr,
    );
    if (confirmed) cubit.disable();
  }

  Future<void> _reactivate(BuildContext context, UserActionsCubit cubit) async {
    final confirmed = await showConfirmDialog(
      context,
      title: 'usersConfirmReactivateTitle'.tr,
      message: 'usersConfirmReactivateMessage'.tr,
    );
    if (confirmed) cubit.reactivate();
  }

  Future<void> _resetPassword(BuildContext context, UserActionsCubit cubit) async {
    final confirmed = await showConfirmDialog(
      context,
      title: 'usersConfirmResetTitle'.tr,
      message: 'usersConfirmResetMessage'.tr,
    );
    if (!confirmed) return;
    await cubit.resetPassword();
    if (context.mounted) await showDefaultPasswordDialog(context, isReset: true);
  }

  @override
  Widget build(BuildContext context) {
    final theme = context.watch<ThemeCubit>().state.appTheme;
    final cubit = context.read<UserActionsCubit>();
    final state = context.watch<UserActionsCubit>().state;

    if (state.status == UserActionsStatus.loading) {
      return LoadingStateWidget(label: 'usersLoading'.tr);
    }
    if (state.status == UserActionsStatus.error || state.user == null) {
      return ErrorStateWidget(title: 'usersLoadError'.tr);
    }

    final user = state.user!;
    final isRunning = state.actionStatus == UserActionStatus.running;

    return Padding(
      padding: AppSpacing.allLg,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        mainAxisSize: MainAxisSize.min,
        children: [
          Text(user.loginIdentifier, style: TextStyleHelper.h3.copyWith(color: theme.textPrimaryColor)),
          SizedBox(height: AppSpacing.sm),
          Row(
            children: [
              Expanded(
                child: Text(
                  user.roles.join(', '),
                  style: TextStyleHelper.bodyMd.copyWith(color: theme.textSecondaryColor),
                ),
              ),
              UserStatusBadge(status: user.status),
            ],
          ),
          SizedBox(height: AppSpacing.lg),
          if (user.status == UserAccountStatus.active)
            AppButton(
              key: const Key('user_details_disable'),
              label: 'usersActionDisable'.tr,
              variant: AppButtonVariant.secondary,
              isFullWidth: true,
              isLoading: isRunning,
              onPressed: () => _disable(context, cubit),
            )
          else
            AppButton(
              key: const Key('user_details_reactivate'),
              label: 'usersActionReactivate'.tr,
              variant: AppButtonVariant.secondary,
              isFullWidth: true,
              isLoading: isRunning,
              onPressed: () => _reactivate(context, cubit),
            ),
          SizedBox(height: AppSpacing.md),
          AppButton(
            key: const Key('user_details_reset_password'),
            label: 'usersActionResetPassword'.tr,
            variant: AppButtonVariant.text,
            isFullWidth: true,
            isLoading: isRunning,
            onPressed: () => _resetPassword(context, cubit),
          ),
          // FR-AUTH-004: no delete action — disable is the only removal path.
        ],
      ),
    );
  }
}
