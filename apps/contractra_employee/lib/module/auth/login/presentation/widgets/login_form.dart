import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../../../../core/language/locales.dart';
import '../../../../../core/theme/app_spacing.dart';
import '../../../../../core/theme/app_theme.dart';
import '../../../../../utilities/text_style_helper.dart';
import '../../../../../widget/app_button.dart';
import '../../../../../widget/app_text_field.dart';

/// Single-column login form that fills the available height, with the
/// submit button pinned to the bottom. Scrolls when the keyboard is open.
class LoginForm extends StatefulWidget {
  const LoginForm({
    super.key,
    required this.identifierController,
    required this.passwordController,
    required this.onSubmit,
    this.isSubmitting = false,
    this.showValidationError = false,
  });

  final TextEditingController identifierController;
  final TextEditingController passwordController;
  final VoidCallback onSubmit;
  final bool isSubmitting;
  final bool showValidationError;

  @override
  State<LoginForm> createState() => _LoginFormState();
}

class _LoginFormState extends State<LoginForm> {
  bool _obscurePassword = true;

  @override
  Widget build(BuildContext context) {
    final theme = context.watch<ThemeCubit>().state.appTheme;

    return CustomScrollView(
      slivers: [
        SliverFillRemaining(
          hasScrollBody: false,
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              SizedBox(height: AppSpacing.xl),
              Icon(Icons.agriculture_outlined, size: 48, color: theme.primaryColor),
              SizedBox(height: AppSpacing.md),
              Text(
                'loginTitle'.tr,
                textAlign: TextAlign.center,
                style: TextStyleHelper.h2.copyWith(color: theme.textPrimaryColor),
              ),
              SizedBox(height: AppSpacing.xs),
              Text(
                'loginSubtitle'.tr,
                textAlign: TextAlign.center,
                style: TextStyleHelper.bodyMd.copyWith(color: theme.textSecondaryColor),
              ),
              SizedBox(height: AppSpacing.xl),
              AppTextField(
                key: const Key('login_identifier'),
                label: 'loginIdentifierLabel'.tr,
                controller: widget.identifierController,
                enabled: !widget.isSubmitting,
                textInputAction: TextInputAction.next,
                prefixIcon: Icons.person_outline,
              ),
              SizedBox(height: AppSpacing.lg),
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
              if (widget.showValidationError) ...[
                SizedBox(height: AppSpacing.sm),
                Text(
                  'loginFieldsRequired'.tr,
                  key: const Key('login_validation_error'),
                  style: TextStyleHelper.bodySm.copyWith(color: theme.dangerColor),
                ),
              ],
              Align(
                alignment: AlignmentDirectional.centerEnd,
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
              const Spacer(),
              SizedBox(height: AppSpacing.lg),
              AppButton(
                key: const Key('login_submit'),
                label: 'loginSubmit'.tr,
                onPressed: widget.onSubmit,
                isLoading: widget.isSubmitting,
                isFullWidth: true,
              ),
              SizedBox(height: AppSpacing.lg),
            ],
          ),
        ),
      ],
    );
  }
}
