import 'package:flutter_test/flutter_test.dart';

import 'package:contractra_admin/core/session/session_cubit.dart';
import 'package:contractra_admin/module/auth/data_layer/models/current_user.dart';
import 'package:contractra_admin/utilities/router_config.dart';

const _user = CurrentUser(
  id: 'u1',
  loginIdentifier: 'admin',
  roles: ['Admin'],
  permissions: ['Users.Manage'],
  mustChangePassword: false,
);

void main() {
  group('redirectForSessionState', () {
    const publicRoutes = ['/login'];
    const protectedRoutes = ['/', '/dashboard', '/parties', '/change-password'];

    for (final route in [...publicRoutes, ...protectedRoutes]) {
      test('SessionUnknown never redirects away from $route', () {
        expect(redirectForSessionState(const SessionUnknown(), route), isNull);
      });
    }

    test('SessionUnauthenticated allows /login', () {
      expect(redirectForSessionState(const SessionUnauthenticated(), '/login'), isNull);
    });

    for (final route in ['/', '/dashboard', '/parties', '/change-password']) {
      test('SessionUnauthenticated redirects $route to /login', () {
        expect(redirectForSessionState(const SessionUnauthenticated(), route), '/login');
      });
    }

    test('SessionAuthenticating never redirects (mid-submit on /login)', () {
      expect(redirectForSessionState(const SessionAuthenticating(), '/login'), isNull);
    });

    test('SessionPasswordChangeRequired allows /change-password', () {
      expect(
        redirectForSessionState(const SessionPasswordChangeRequired(_user), '/change-password'),
        isNull,
      );
    });

    for (final route in ['/', '/login', '/dashboard', '/parties']) {
      test('SessionPasswordChangeRequired redirects $route to /change-password (no escape)', () {
        expect(
          redirectForSessionState(const SessionPasswordChangeRequired(_user), route),
          '/change-password',
        );
      });
    }

    for (final route in ['/', '/login', '/change-password']) {
      test('SessionAuthenticated redirects $route to the landing route', () {
        expect(redirectForSessionState(const SessionAuthenticated(_user), route), landingRoute);
      });
    }

    for (final route in ['/dashboard', '/parties', '/operations', '/approvals', '/reports']) {
      test('SessionAuthenticated allows the protected module route $route', () {
        expect(redirectForSessionState(const SessionAuthenticated(_user), route), isNull);
      });
    }
  });
}
