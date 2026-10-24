import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:go_router/go_router.dart';

import '../../../../core/language/locales.dart';
import '../../../../core/network/failures.dart';
import '../../../../core/theme/app_spacing.dart';
import '../../../../core/theme/app_theme.dart';
import '../../../../utilities/git_it.dart';
import '../../../../utilities/text_style_helper.dart';
import '../../../../widget/app_button.dart';
import '../../../../widget/app_text_field.dart';
import '../../../../widget/loading_state_widget.dart';
import '../../cubit/new_user_cubit.dart';
import '../../data_layer/repository/users_repository.dart';
import '../widgets/default_password_dialog.dart';
import '../widgets/role_multi_select.dart';

class NewUserScreen extends StatelessWidget {
  const NewUserScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return BlocProvider(
      create: (_) => NewUserCubit(GitIt.instance<UsersRepository>()),
      child: const _NewUserView(),
    );
  }
}

String? _serverErrorMessage(Failure failure) => switch (failure) {
      LoginIdentifierTakenFailure() => 'usersIdentifierTaken'.tr,
      RoleRequiredFailure() => 'usersRoleRequired'.tr,
      NetworkFailure() => 'errorNetwork'.tr,
      _ => 'errorGeneric'.tr,
    };

String? _validationMessage(NewUserValidationReason reason) => switch (reason) {
      NewUserValidationReason.identifierEmpty => 'usersIdentifierRequired'.tr,
      NewUserValidationReason.noRoleSelected => 'usersRoleRequired'.tr,
    };

class _NewUserView extends StatefulWidget {
  const _NewUserView();

  @override
  State<_NewUserView> createState() => _NewUserViewState();
}

class _NewUserViewState extends State<_NewUserView> {
  final _identifierController = TextEditingController();
  bool _dialogShown = false;

  @override
  void dispose() {
    _identifierController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final theme = context.watch<ThemeCubit>().state.appTheme;
    final cubit = context.read<NewUserCubit>();
    final state = context.watch<NewUserCubit>().state;

    if (state.submitStatus == SubmitStatus.success && !_dialogShown) {
      _dialogShown = true;
      WidgetsBinding.instance.addPostFrameCallback((_) async {
        if (!context.mounted) return;
        await showDefaultPasswordDialog(context, isReset: false);
        if (context.mounted) context.pop();
      });
    }

    if (state.rolesStatus == RolesLoadStatus.loading) {
      return LoadingStateWidget(label: 'usersLoading'.tr);
    }

    return Padding(
      padding: AppSpacing.allLg,
      child: SingleChildScrollView(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text(
              'usersNewUserTitle'.tr,
              style: TextStyleHelper.h3.copyWith(color: theme.textPrimaryColor),
            ),
            SizedBox(height: AppSpacing.lg),
            AppTextField(
              key: const Key('new_user_identifier'),
              label: 'usersColumnIdentifier'.tr,
              controller: _identifierController,
              enabled: state.submitStatus != SubmitStatus.submitting,
              prefixIcon: Icons.person_outline,
            ),
            SizedBox(height: AppSpacing.lg),
            RoleMultiSelect(
              label: 'usersRolesLabel'.tr,
              roles: state.availableRoles,
              selectedCodes: state.selectedRoleCodes,
              onToggle: cubit.toggleRole,
            ),
            if (state.validationError != null) ...[
              SizedBox(height: AppSpacing.sm),
              Text(
                _validationMessage(state.validationError!) ?? '',
                key: const Key('new_user_error'),
                style: TextStyleHelper.bodySm.copyWith(color: theme.dangerColor),
              ),
            ],
            if (state.serverError != null) ...[
              SizedBox(height: AppSpacing.sm),
              Text(
                _serverErrorMessage(state.serverError!) ?? '',
                key: const Key('new_user_error'),
                style: TextStyleHelper.bodySm.copyWith(color: theme.dangerColor),
              ),
            ],
            SizedBox(height: AppSpacing.lg),
            AppButton(
              key: const Key('new_user_submit'),
              label: 'usersCreateSubmit'.tr,
              isLoading: state.submitStatus == SubmitStatus.submitting,
              isFullWidth: true,
              onPressed: () => cubit.submit(_identifierController.text),
            ),
          ],
        ),
      ),
    );
  }
}
