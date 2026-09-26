import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../../../core/language/locales.dart';
import '../../../../core/session/session_cubit.dart';
import '../../../../core/theme/app_breakpoints.dart';
import '../../../../core/theme/app_theme.dart';
import '../cubit/login_cubit.dart';
import 'screens_layout/l_login_screen.dart';
import 'screens_layout/s_login_screen.dart';
import 'widgets/login_form.dart';

class LoginScreen extends StatelessWidget {
  const LoginScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return BlocProvider(
      create: (context) => LoginCubit(context.read<SessionCubit>()),
      child: const LoginView(),
    );
  }
}

String? _errorMessageFor(LoginState state) => switch (state) {
      LoginValidationError() => 'loginFieldsRequired'.tr,
      LoginInvalidCredentials() => 'loginInvalidCredentials'.tr,
      LoginAccountDisabled() => 'loginAccountDisabled'.tr,
      LoginNetworkError() => 'errorNetwork'.tr,
      LoginIdle() || LoginSubmitting() || LoginSucceeded() => null,
    };

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
      isSubmitting: state is LoginSubmitting || state is LoginSucceeded,
      errorMessage: _errorMessageFor(state),
    );

    return Scaffold(
      backgroundColor: theme.backgroundColor,
      body: SafeArea(
        child: MediaQuery.sizeOf(context).width >= AppBreakpoints.large
            ? LargeLoginScreen(form: form)
            : SmallLoginScreen(form: form),
      ),
    );
  }
}
