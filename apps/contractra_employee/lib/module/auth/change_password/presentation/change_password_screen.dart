import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../../../core/theme/app_spacing.dart';
import '../../../../core/theme/app_theme.dart';
import '../../../../utilities/git_it.dart';
import '../../data_layer/repository/auth_repository.dart';
import '../../../../core/session/session_cubit.dart';
import '../cubit/change_password_cubit.dart';
import 'widgets/change_password_form.dart';

class ChangePasswordScreen extends StatelessWidget {
  const ChangePasswordScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return BlocProvider(
      create: (context) => ChangePasswordCubit(
        GitIt.instance<AuthRepository>(),
        context.read<SessionCubit>(),
      ),
      child: const _ChangePasswordView(),
    );
  }
}

class _ChangePasswordView extends StatelessWidget {
  const _ChangePasswordView();

  @override
  Widget build(BuildContext context) {
    final theme = context.watch<ThemeCubit>().state.appTheme;

    return Scaffold(
      backgroundColor: theme.backgroundColor,
      body: SafeArea(
        child: Center(
          child: SingleChildScrollView(
            padding: AppSpacing.allXl,
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 440),
              child: const ChangePasswordForm(),
            ),
          ),
        ),
      ),
    );
  }
}
