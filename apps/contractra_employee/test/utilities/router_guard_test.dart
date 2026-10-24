import 'package:flutter_test/flutter_test.dart';

import 'package:contractra_employee/core/session/session_cubit.dart';
import 'package:contractra_employee/module/auth/data_layer/models/current_user.dart';
import 'package:contractra_employee/utilities/router_config.dart';

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
    const protectedRoutes = ['/', '/assignments', '/expenses', '/change-password'];

    for (final route in [...publicRoutes, ...protectedRoutes]) {
      test('SessionUnknown never redirects away from $route', () {
        expect(redirectForSessionState(const SessionUnknown(), route), isNull);
      });
    }

    test('SessionUnauthenticated allows /login', () {
      expect(redirectForSessionState(const SessionUnauthenticated(), '/login'), isNull);
    });

    for (final route in ['/', '/assignments', '/expenses', '/change-password']) {
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

    for (final route in ['/', '/login', '/assignments', '/expenses']) {
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

    for (final route in ['/assignments', '/expenses', '/profile']) {
      test('SessionAuthenticated allows the protected module route $route', () {
        expect(redirectForSessionState(const SessionAuthenticated(_user), route), isNull);
      });
    }
  });
}
