import 'dart:async';

import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../core/session/session_cubit.dart';
import '../module/approvals/presentation/screens_layout/approvals_placeholder_screen.dart';
import '../module/auth/change_password/presentation/change_password_screen.dart';
import '../module/auth/login/presentation/login_screen.dart';
import '../module/auth/splash/presentation/splash_screen.dart';
import '../module/dashboard/presentation/screens_layout/dashboard_placeholder_screen.dart';
import '../module/operations/presentation/screens_layout/operations_placeholder_screen.dart';
import '../module/parties/presentation/screens_layout/party_directory_screen.dart';
import '../module/reports/presentation/screens_layout/reports_placeholder_screen.dart';
import '../module/users/presentation/screens_layout/new_user_screen.dart';
import '../module/users/presentation/screens_layout/user_details_screen.dart';
import '../module/users/presentation/screens_layout/users_list_screen.dart';
import 'app_shell_screen.dart';

const landingRoute = '/dashboard';

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
          GoRoute(
            path: '/users',
            pageBuilder: (_, GoRouterState state) => getCustomTransitionPage(
              state: state,
              child: const UsersListScreen(),
            ),
            routes: [
              GoRoute(
                path: 'new',
                pageBuilder: (_, GoRouterState state) => getCustomTransitionPage(
                  state: state,
                  child: const NewUserScreen(),
                ),
              ),
              GoRoute(
                path: ':id',
                pageBuilder: (_, GoRouterState state) => getCustomTransitionPage(
                  state: state,
                  child: UserDetailsScreen(userId: state.pathParameters['id']!),
                ),
              ),
            ],
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

/// Session-state-driven guard, independent of go_router's plumbing so it
/// can be unit tested directly against a location string.
///
/// [SessionUnknown] never redirects — splash owns that window while it
/// tries to restore a session. Everything else is decided from
/// [SessionState] alone; a route not covered below (splash, /) always
/// yields to a more specific check. `/users` additionally requires
/// `Users.Manage` (US-011) — enforced here independently of whether the
/// nav item happens to be shown, never relying on the UI alone.
String? redirectForSessionState(SessionState state, String location) {
  final isPublicAuthRoute = location == '/login';
  final isChangePasswordRoute = location == '/change-password';
  final isSplash = location == '/';
  final isUsersRoute = location == '/users' || location.startsWith('/users/');

  switch (state) {
    case SessionUnknown():
      return null;
    case SessionUnauthenticated():
      return isPublicAuthRoute ? null : '/login';
    case SessionAuthenticating():
      return null;
    case SessionPasswordChangeRequired():
      return isChangePasswordRoute ? null : '/change-password';
    case SessionAuthenticated(user: final user):
      if (isPublicAuthRoute || isChangePasswordRoute || isSplash) return landingRoute;
      if (isUsersRoute && !user.hasPermission(usersManagePermission)) return landingRoute;
      return null;
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
