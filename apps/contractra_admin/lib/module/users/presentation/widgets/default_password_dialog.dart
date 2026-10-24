import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../../../core/language/locales.dart';
import '../../../../core/theme/app_theme.dart';
import '../../../../utilities/text_style_helper.dart';
import '../../../../widget/app_button.dart';
import '../../../auth/change_password/cubit/change_password_cubit.dart' show defaultPassword;

/// Shared between "new user created" and "password reset" — the default
/// password appears only here (frontend-auth-and-users-plan.md, gap G2;
/// not documented in any authoritative source, implemented per
/// claude-code-prompt-02-frontend.md).
Future<void> showDefaultPasswordDialog(BuildContext context, {required bool isReset}) {
  return showDialog<void>(
    context: context,
    builder: (dialogContext) => DefaultPasswordDialog(isReset: isReset),
  );
}

class DefaultPasswordDialog extends StatelessWidget {
  const DefaultPasswordDialog({super.key, required this.isReset});

  final bool isReset;

  @override
  Widget build(BuildContext context) {
    final theme = context.watch<ThemeCubit>().state.appTheme;

    return AlertDialog(
      backgroundColor: theme.surfaceColor,
      title: Text(
        isReset ? 'usersResetPasswordDialogTitle'.tr : 'usersCreatedDialogTitle'.tr,
        style: TextStyleHelper.h4.copyWith(color: theme.textPrimaryColor),
      ),
      content: Text(
        'usersDefaultPasswordMessage'.tr.replaceAll('{password}', defaultPassword),
        style: TextStyleHelper.bodyMd.copyWith(color: theme.textSecondaryColor),
      ),
      actions: [
        AppButton(
          key: const Key('default_password_dialog_ok'),
          label: 'ok'.tr,
          onPressed: () => Navigator.of(context).pop(),
        ),
      ],
    );
  }
}
