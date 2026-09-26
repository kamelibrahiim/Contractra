import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../core/theme/app_spacing.dart';
import '../core/theme/app_theme.dart';
import '../utilities/text_style_helper.dart';

/// Generic in-progress placeholder shown while a feature's cubit is
/// fetching data. Presentation only — no data-fetching logic lives here.
class LoadingStateWidget extends StatelessWidget {
  const LoadingStateWidget({super.key, this.label});

  final String? label;

  @override
  Widget build(BuildContext context) {
    final theme = context.watch<ThemeCubit>().state.appTheme;

    return Center(
      child: Padding(
        padding: AppSpacing.allLg,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            CircularProgressIndicator(
              valueColor: AlwaysStoppedAnimation<Color>(theme.primaryColor),
            ),
            if (label != null) ...[
              SizedBox(height: AppSpacing.md),
              Text(
                label!,
                textAlign: TextAlign.center,
                style: TextStyleHelper.bodyMd.copyWith(
                  color: theme.textSecondaryColor,
                ),
              ),
            ],
          ],
        ),
      ),
    );
  }
}
