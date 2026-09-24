import 'package:flutter/material.dart';

import '../../../../core/theme/app_theme.dart';

/// Structural placeholder so the app has a valid entry route.
/// Replaced by the real splash / bootstrap flow in Sprint 1.
class SplashScreen extends StatelessWidget {
  const SplashScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final theme = appTheme(context);
    return Scaffold(
      backgroundColor: theme.backGroundColor,
      body: const Center(
        child: Text('Contractra Admin'),
      ),
    );
  }
}
