import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../../../../core/language/locales.dart';
import '../../../../../core/theme/app_spacing.dart';
import '../../../../../core/theme/app_theme.dart';
import '../../../../../utilities/text_style_helper.dart';
import '../../../../../widget/app_card.dart';

/// Large (web) layout: a branding panel beside a centered form card.
class LargeLoginScreen extends StatelessWidget {
  const LargeLoginScreen({super.key, required this.form});

  final Widget form;

  @override
  Widget build(BuildContext context) {
    final theme = context.watch<ThemeCubit>().state.appTheme;

    return Row(
      children: [
        Expanded(
          flex: 5,
          child: ColoredBox(
            color: theme.primaryColor,
            child: Padding(
              padding: AppSpacing.allXl,
              child: Column(
                mainAxisAlignment: MainAxisAlignment.center,
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Icon(Icons.agriculture_outlined, size: 56, color: theme.surfaceColor),
                  SizedBox(height: AppSpacing.lg),
                  Text(
                    'appTitle'.tr,
                    style: TextStyleHelper.h1.copyWith(color: theme.surfaceColor),
                  ),
                  SizedBox(height: AppSpacing.sm),
                  Text(
                    'loginBrandingTagline'.tr,
                    style: TextStyleHelper.bodyLg.copyWith(
                      color: theme.surfaceColor.withValues(alpha: 0.85),
                    ),
                  ),
                ],
              ),
            ),
          ),
        ),
        Expanded(
          flex: 6,
          child: Center(
            child: SingleChildScrollView(
              padding: AppSpacing.allXl,
              child: ConstrainedBox(
                constraints: const BoxConstraints(maxWidth: 440),
                child: AppCard(padding: AppSpacing.allXl, child: form),
              ),
            ),
          ),
        ),
      ],
    );
  }
}
