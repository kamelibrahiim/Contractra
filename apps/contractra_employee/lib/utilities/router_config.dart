import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../module/assignments/presentation/screens_layout/assignments_placeholder_screen.dart';
import '../module/auth/login/presentation/login_screen.dart';
import '../module/auth/splash/presentation/splash_screen.dart';
import '../module/expenses/presentation/screens_layout/expenses_placeholder_screen.dart';
import '../module/profile/presentation/screens_layout/profile_placeholder_screen.dart';
import 'app_shell_screen.dart';

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
      GoRoute(
        path: '/login',
        pageBuilder: (_, GoRouterState state) => getCustomTransitionPage(
          state: state,
          child: const LoginScreen(),
        ),
      ),
      StatefulShellRoute.indexedStack(
        builder: (_, _, navigationShell) =>
            AppShellScreen(navigationShell: navigationShell),
        branches: [
          _branch('/assignments', const AssignmentsPlaceholderScreen()),
          _branch('/expenses', const ExpensesPlaceholderScreen()),
          _branch('/profile', const ProfilePlaceholderScreen()),
        ],
      ),
    ],
  );

  static StatefulShellBranch _branch(String path, Widget screen) =>
      StatefulShellBranch(
        routes: [
          GoRoute(
            path: path,
            pageBuilder: (_, GoRouterState state) =>
                getCustomTransitionPage(state: state, child: screen),
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
