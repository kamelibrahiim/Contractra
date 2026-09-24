import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../module/auth/splash/presentation/splash_screen.dart';

/// Sprint 1 adds the real routes (login, then each module's landing route)
/// here. Only the entry point exists at scaffolding time.
BuildContext? get currentContext =>
    GoRouterConfig.router.routerDelegate.navigatorKey.currentContext;

class GoRouterConfig {
  static GoRouter get router => _router;
  static final GoRouter _router = GoRouter(
    routes: <RouteBase>[
      GoRoute(
        path: '/',
        pageBuilder: (_, GoRouterState state) => getCustomTransitionPage(
          state: state,
          child: const SplashScreen(),
        ),
      ),
    ],
  );

  static CustomTransitionPage getCustomTransitionPage({
    required GoRouterState state,
    required Widget child,
  }) {
    return CustomTransitionPage(
      key: state.pageKey,
      child: child,
      transitionDuration: const Duration(milliseconds: 300),
      reverseTransitionDuration: const Duration(milliseconds: 300),
      transitionsBuilder: (context, animation, secondaryAnimation, child) {
        return FadeTransition(
          opacity: CurveTween(curve: Curves.easeInOutCirc).animate(animation),
          child: child,
        );
      },
    );
  }
}
