import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../../../../core/language/locales.dart';
import '../../../../../core/network/failures.dart';
import '../../../../../core/theme/app_spacing.dart';
import '../../../../../core/theme/app_theme.dart';
import '../../../../../utilities/text_style_helper.dart';
import '../../../../../widget/app_button.dart';
import '../../../../../widget/app_text_field.dart';
import '../../cubit/change_password_cubit.dart';

String _validationMessage(ChangePasswordValidationReason reason) => switch (reason) {
      ChangePasswordValidationReason.tooShort => 'changePasswordTooShort'.tr,
      ChangePasswordValidationReason.isDefaultPassword => 'changePasswordIsDefault'.tr,
      ChangePasswordValidationReason.doesNotMatch => 'changePasswordMismatch'.tr,
    };

String _serverErrorMessage(Failure failure) => switch (failure) {
      InvalidCredentialsFailure() => 'changePasswordWrongCurrent'.tr,
      NetworkFailure() => 'errorNetwork'.tr,
      _ => 'errorGeneric'.tr,
    };

class ChangePasswordForm extends StatefulWidget {
  const ChangePasswordForm({super.key});

  @override
  State<ChangePasswordForm> createState() => _ChangePasswordFormState();
}

class _ChangePasswordFormState extends State<ChangePasswordForm> {
  final _currentController = TextEditingController();
  final _newController = TextEditingController();
  final _confirmController = TextEditingController();
  bool _obscureCurrent = true;
  bool _obscureNew = true;
  bool _obscureConfirm = true;

  @override
  void dispose() {
    _currentController.dispose();
    _newController.dispose();
    _confirmController.dispose();
    super.dispose();
  }

  void _submit() => context.read<ChangePasswordCubit>().submit(
        currentPassword: _currentController.text,
        newPassword: _newController.text,
        confirmPassword: _confirmController.text,
      );

  @override
  Widget build(BuildContext context) {
    final theme = context.watch<ThemeCubit>().state.appTheme;
    final state = context.watch<ChangePasswordCubit>().state;
    final isSubmitting = state is ChangePasswordSubmitting;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      mainAxisSize: MainAxisSize.min,
      children: [
        Text(
          'changePasswordTitle'.tr,
          style: TextStyleHelper.h2.copyWith(color: theme.textPrimaryColor),
        ),
        SizedBox(height: AppSpacing.xs),
        Text(
          'changePasswordSubtitle'.tr,
          style: TextStyleHelper.bodyMd.copyWith(color: theme.textSecondaryColor),
        ),
        SizedBox(height: AppSpacing.lg),
        AppTextField(
          key: const Key('change_password_current'),
          label: 'changePasswordCurrentLabel'.tr,
          controller: _currentController,
          enabled: !isSubmitting,
          obscureText: _obscureCurrent,
          prefixIcon: Icons.lock_outline,
          suffixIcon: _obscureCurrent ? Icons.visibility_outlined : Icons.visibility_off_outlined,
          onSuffixIconPressed: () => setState(() => _obscureCurrent = !_obscureCurrent),
        ),
        SizedBox(height: AppSpacing.md),
        AppTextField(
          key: const Key('change_password_new'),
          label: 'changePasswordNewLabel'.tr,
          controller: _newController,
          enabled: !isSubmitting,
          obscureText: _obscureNew,
          prefixIcon: Icons.lock_outline,
          suffixIcon: _obscureNew ? Icons.visibility_outlined : Icons.visibility_off_outlined,
          onSuffixIconPressed: () => setState(() => _obscureNew = !_obscureNew),
        ),
        SizedBox(height: AppSpacing.md),
        AppTextField(
          key: const Key('change_password_confirm'),
          label: 'changePasswordConfirmLabel'.tr,
          controller: _confirmController,
          enabled: !isSubmitting,
          obscureText: _obscureConfirm,
          textInputAction: TextInputAction.done,
          onFieldSubmitted: (_) => _submit(),
          prefixIcon: Icons.lock_outline,
          suffixIcon: _obscureConfirm ? Icons.visibility_outlined : Icons.visibility_off_outlined,
          onSuffixIconPressed: () => setState(() => _obscureConfirm = !_obscureConfirm),
        ),
        if (state is ChangePasswordValidationError) ...[
          SizedBox(height: AppSpacing.sm),
          Text(
            _validationMessage(state.reason),
            key: const Key('change_password_error'),
            style: TextStyleHelper.bodySm.copyWith(color: theme.dangerColor),
          ),
        ],
        if (state is ChangePasswordServerError) ...[
          SizedBox(height: AppSpacing.sm),
          Text(
            _serverErrorMessage(state.failure),
            key: const Key('change_password_error'),
            style: TextStyleHelper.bodySm.copyWith(color: theme.dangerColor),
          ),
        ],
        SizedBox(height: AppSpacing.lg),
        AppButton(
          key: const Key('change_password_submit'),
          label: 'changePasswordSubmit'.tr,
          onPressed: _submit,
          isLoading: isSubmitting,
          isFullWidth: true,
        ),
      ],
    );
  }
}
