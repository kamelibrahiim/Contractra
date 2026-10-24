import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';

import '../../../../core/language/locales.dart';
import '../../../../core/session/session_cubit.dart';
import '../../../../core/theme/app_theme.dart';
import '../../../../utilities/text_style_helper.dart';

/// Restores an existing session (refresh token, then `/auth/me`) before the
/// router's guard redirects away from here. The router's redirect handles
/// every outcome; this screen only triggers the restore attempt.
class SplashScreen extends StatefulWidget {
  const SplashScreen({super.key});

  @override
  State<SplashScreen> createState() => _SplashScreenState();
}

class _SplashScreenState extends State<SplashScreen> {
  @override
  void initState() {
    super.initState();
    context.read<SessionCubit>().restore();
  }

  @override
  Widget build(BuildContext context) {
    final theme = appTheme(context);
    return Scaffold(
      backgroundColor: theme.backgroundColor,
      body: Center(
        child: Text(
          'appTitle'.tr,
          style: TextStyleHelper.h3.copyWith(color: theme.primaryColor),
        ),
      ),
    );
  }
}
