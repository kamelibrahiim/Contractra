import 'dart:async';

import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../../../core/language/locales.dart';
import '../../../../core/theme/app_theme.dart';
import '../../../../utilities/text_style_helper.dart';

/// Brief transient screen before the login route. No session restore yet —
/// that arrives with the real Users/Auth integration.
class SplashScreen extends StatefulWidget {
  const SplashScreen({super.key});

  @override
  State<SplashScreen> createState() => _SplashScreenState();
}

class _SplashScreenState extends State<SplashScreen> {
  Timer? _timer;

  @override
  void initState() {
    super.initState();
    _timer = Timer(const Duration(milliseconds: 600), () {
      if (mounted) context.go('/login');
    });
  }

  @override
  void dispose() {
    _timer?.cancel();
    super.dispose();
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
