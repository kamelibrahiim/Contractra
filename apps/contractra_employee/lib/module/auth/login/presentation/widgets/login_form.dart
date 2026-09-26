import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../../../../core/language/locales.dart';
import '../../../../../core/theme/app_spacing.dart';
import '../../../../../core/theme/app_theme.dart';
import '../../../../../utilities/text_style_helper.dart';
import '../../../../../widget/app_button.dart';
import '../../../../../widget/app_text_field.dart';

/// Identifier + password + submit + (disabled) forgot-password link.
/// Holds no credentials itself; the fields' values stay in the controllers.
class LoginForm extends StatefulWidget {
  const LoginForm({
    super.key,
    required this.identifierController,
    required this.passwordController,
    required this.onSubmit,
    this.isSubmitting = false,
    this.errorMessage,
  });

  final TextEditingController identifierController;
  final TextEditingController passwordController;
  final VoidCallback onSubmit;
  final bool isSubmitting;

  /// Any local or server-originated message to show under the fields.
  /// UC-001's exception flow: this never distinguishes which field is
  /// wrong for invalid-credentials cases — only the caller decides the
  /// exact wording per failure type.
  final String? errorMessage;

  @override
  State<LoginForm> createState() => _LoginFormState();
}

class _LoginFormState extends State<LoginForm> {
  bool _obscurePassword = true;

  @override
  Widget build(BuildContext context) {
    final theme = context.watch<ThemeCubit>().state.appTheme;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      mainAxisSize: MainAxisSize.min,
      children: [
        Text(
          'loginTitle'.tr,
          style: TextStyleHelper.h2.copyWith(color: theme.textPrimaryColor),
        ),
        SizedBox(height: AppSpacing.xs),
        Text(
          'loginSubtitle'.tr,
          style: TextStyleHelper.bodyMd.copyWith(color: theme.textSecondaryColor),
        ),
        SizedBox(height: AppSpacing.lg),
        AppTextField(
          key: const Key('login_identifier'),
          label: 'loginIdentifierLabel'.tr,
          controller: widget.identifierController,
          enabled: !widget.isSubmitting,
          textInputAction: TextInputAction.next,
          prefixIcon: Icons.person_outline,
        ),
        SizedBox(height: AppSpacing.md),
        AppTextField(
          key: const Key('login_password'),
          label: 'loginPasswordLabel'.tr,
          controller: widget.passwordController,
          enabled: !widget.isSubmitting,
          obscureText: _obscurePassword,
          textInputAction: TextInputAction.done,
          onFieldSubmitted: (_) => widget.onSubmit(),
          prefixIcon: Icons.lock_outline,
          suffixIcon: _obscurePassword
              ? Icons.visibility_outlined
              : Icons.visibility_off_outlined,
          onSuffixIconPressed: () =>
              setState(() => _obscurePassword = !_obscurePassword),
        ),
        if (widget.errorMessage != null) ...[
          SizedBox(height: AppSpacing.sm),
          Text(
            widget.errorMessage!,
            key: const Key('login_error'),
            style: TextStyleHelper.bodySm.copyWith(color: theme.dangerColor),
          ),
        ],
        SizedBox(height: AppSpacing.lg),
        AppButton(
          key: const Key('login_submit'),
          label: 'loginSubmit'.tr,
          onPressed: widget.onSubmit,
          isLoading: widget.isSubmitting,
          isFullWidth: true,
        ),
        SizedBox(height: AppSpacing.sm),
        Align(
          alignment: AlignmentDirectional.center,
          child: TextButton(
            key: const Key('login_forgot_password'),
            // No forgot-password flow is documented yet.
            onPressed: null,
            child: Text(
              'loginForgotPassword'.tr,
              style: TextStyleHelper.bodySm.copyWith(color: theme.disabledColor),
            ),
          ),
        ),
      ],
    );
  }
}
