import 'dart:async';

import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../core/session/session_cubit.dart';
import '../module/assignments/presentation/screens_layout/assignments_placeholder_screen.dart';
import '../module/auth/change_password/presentation/change_password_screen.dart';
import '../module/auth/login/presentation/login_screen.dart';
import '../module/auth/splash/presentation/splash_screen.dart';
import '../module/expenses/presentation/screens_layout/expenses_placeholder_screen.dart';
import '../module/profile/presentation/screens_layout/profile_placeholder_screen.dart';
import 'app_shell_screen.dart';

const landingRoute = '/assignments';

BuildContext? get currentContext =>
    GoRouterConfig.router.routerDelegate.navigatorKey.currentContext;

class GoRouterConfig {
  /// Must be called with the app's singleton [SessionCubit] before [router]
  /// is first read (see main.dart's composition root). Safe to call more
  /// than once (e.g. from `build()`); only the first call takes effect.
  static void init(SessionCubit sessionCubit) {
    _router ??= _build(sessionCubit);
  }

  static GoRouter get router {
    assert(_router != null, 'GoRouterConfig.init() must run before router is used.');
    return _router!;
  }

  static GoRouter? _router;

  static GoRouter _build(SessionCubit sessionCubit) => GoRouter(
        refreshListenable: _SessionListenable(sessionCubit),
        redirect: (context, state) => _redirect(sessionCubit.state, state.matchedLocation),
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
          GoRoute(
            path: '/change-password',
            pageBuilder: (_, GoRouterState state) => getCustomTransitionPage(
              state: state,
              child: const ChangePasswordScreen(),
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

/// Session-state-driven guard, independent of go_router's plumbing so it
/// can be unit tested directly against a location string.
///
/// [SessionUnknown] never redirects — splash owns that window while it
/// tries to restore a session. Everything else is decided from
/// [SessionState] alone; a route not covered below (splash, /) always
/// yields to a more specific check.
String? redirectForSessionState(SessionState state, String location) {
  final isPublicAuthRoute = location == '/login';
  final isChangePasswordRoute = location == '/change-password';
  final isSplash = location == '/';

  switch (state) {
    case SessionUnknown():
      return null;
    case SessionUnauthenticated():
      return isPublicAuthRoute ? null : '/login';
    case SessionAuthenticating():
      return null;
    case SessionPasswordChangeRequired():
      return isChangePasswordRoute ? null : '/change-password';
    case SessionAuthenticated():
      return (isPublicAuthRoute || isChangePasswordRoute || isSplash) ? landingRoute : null;
  }
}

String? _redirect(SessionState state, String location) =>
    redirectForSessionState(state, location);

/// Bridges [SessionCubit]'s stream to the `Listenable` go_router's
/// `refreshListenable` expects.
class _SessionListenable extends ChangeNotifier {
  _SessionListenable(SessionCubit cubit) {
    _subscription = cubit.stream.listen((_) => notifyListeners());
  }

  late final StreamSubscription<SessionState> _subscription;

  @override
  void dispose() {
    _subscription.cancel();
    super.dispose();
  }
}
