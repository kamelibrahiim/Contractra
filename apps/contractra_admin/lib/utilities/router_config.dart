import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../module/approvals/presentation/screens_layout/approvals_placeholder_screen.dart';
import '../module/auth/login/presentation/login_screen.dart';
import '../module/auth/splash/presentation/splash_screen.dart';
import '../module/dashboard/presentation/screens_layout/dashboard_placeholder_screen.dart';
import '../module/operations/presentation/screens_layout/operations_placeholder_screen.dart';
import '../module/parties/presentation/screens_layout/party_directory_screen.dart';
import '../module/reports/presentation/screens_layout/reports_placeholder_screen.dart';
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
          _branch('/dashboard', const DashboardPlaceholderScreen()),
          _branch('/parties', const PartyDirectoryScreen()),
          _branch('/operations', const OperationsPlaceholderScreen()),
          _branch('/approvals', const ApprovalsPlaceholderScreen()),
          _branch('/reports', const ReportsPlaceholderScreen()),
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
