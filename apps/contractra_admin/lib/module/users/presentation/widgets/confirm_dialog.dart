import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../../../core/language/locales.dart';
import '../../../../core/theme/app_theme.dart';
import '../../../../utilities/text_style_helper.dart';
import '../../../../widget/app_button.dart';

/// Generic yes/no confirmation. Used before disable, reactivate, and
/// reset-password — none of these actions runs without it.
Future<bool> showConfirmDialog(
  BuildContext context, {
  required String title,
  required String message,
}) async {
  final confirmed = await showDialog<bool>(
    context: context,
    builder: (dialogContext) => ConfirmDialog(title: title, message: message),
  );
  return confirmed ?? false;
}

class ConfirmDialog extends StatelessWidget {
  const ConfirmDialog({super.key, required this.title, required this.message});

  final String title;
  final String message;

  @override
  Widget build(BuildContext context) {
    final theme = context.watch<ThemeCubit>().state.appTheme;

    return AlertDialog(
      backgroundColor: theme.surfaceColor,
      title: Text(title, style: TextStyleHelper.h4.copyWith(color: theme.textPrimaryColor)),
      content: Text(message, style: TextStyleHelper.bodyMd.copyWith(color: theme.textSecondaryColor)),
      actions: [
        AppButton(
          key: const Key('confirm_dialog_cancel'),
          label: 'cancel'.tr,
          variant: AppButtonVariant.text,
          onPressed: () => Navigator.of(context).pop(false),
        ),
        AppButton(
          key: const Key('confirm_dialog_confirm'),
          label: 'confirm'.tr,
          onPressed: () => Navigator.of(context).pop(true),
        ),
      ],
    );
  }
}
