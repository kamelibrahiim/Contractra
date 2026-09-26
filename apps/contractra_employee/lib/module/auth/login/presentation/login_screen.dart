import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../../../core/theme/app_breakpoints.dart';
import '../../../../core/theme/app_theme.dart';
import '../cubit/login_cubit.dart';
import 'screens_layout/m_login_screen.dart';
import 'screens_layout/s_login_screen.dart';
import 'widgets/login_form.dart';

class LoginScreen extends StatelessWidget {
  const LoginScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return BlocProvider(
      create: (_) => LoginCubit(),
      child: const LoginView(),
    );
  }
}

/// Expects a [LoginCubit] above it; split out so tests can inject one.
class LoginView extends StatefulWidget {
  const LoginView({super.key});

  @override
  State<LoginView> createState() => _LoginViewState();
}

class _LoginViewState extends State<LoginView> {
  final _identifierController = TextEditingController();
  final _passwordController = TextEditingController();

  @override
  void dispose() {
    _identifierController.dispose();
    _passwordController.dispose();
    super.dispose();
  }

  void _submit() => context
      .read<LoginCubit>()
      .submit(_identifierController.text, _passwordController.text);

  @override
  Widget build(BuildContext context) {
    final theme = context.watch<ThemeCubit>().state.appTheme;
    final state = context.watch<LoginCubit>().state;

    final form = LoginForm(
      identifierController: _identifierController,
      passwordController: _passwordController,
      onSubmit: _submit,
      isSubmitting: state is LoginSubmitting,
      showValidationError: state is LoginValidationError,
    );

    return Scaffold(
      backgroundColor: theme.backgroundColor,
      body: SafeArea(
        child: MediaQuery.sizeOf(context).width >= AppBreakpoints.medium
            ? MediumLoginScreen(form: form)
            : SmallLoginScreen(form: form),
      ),
    );
  }
}
