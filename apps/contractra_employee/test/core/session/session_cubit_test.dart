import 'package:bloc_test/bloc_test.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

import 'package:contractra_employee/core/network/failures.dart';
import 'package:contractra_employee/core/session/session_cubit.dart';
import 'package:contractra_employee/module/auth/data_layer/models/current_user.dart';
import 'package:contractra_employee/module/auth/data_layer/models/login_result.dart';
import 'package:contractra_employee/module/auth/data_layer/repository/auth_repository.dart';

class _MockAuthRepository extends Mock implements AuthRepository {}

const _user = CurrentUser(
  id: 'u1',
  loginIdentifier: 'admin',
  roles: ['Admin'],
  permissions: ['Users.Manage'],
  mustChangePassword: false,
);

const _userMustChange = CurrentUser(
  id: 'u1',
  loginIdentifier: 'admin',
  roles: ['Admin'],
  permissions: ['Users.Manage'],
  mustChangePassword: true,
);

void main() {
  late _MockAuthRepository authRepository;

  setUp(() => authRepository = _MockAuthRepository());

  group('restore', () {
    blocTest<SessionCubit, SessionState>(
      'a valid refresh token leads to authenticated',
      build: () => SessionCubit(authRepository),
      setUp: () {
        when(() => authRepository.refresh()).thenAnswer((_) async => 'new-token');
        when(() => authRepository.me()).thenAnswer((_) async => const AuthSuccess(_user));
      },
      act: (cubit) => cubit.restore(),
      expect: () => [const SessionAuthenticated(_user)],
    );

    blocTest<SessionCubit, SessionState>(
      'mustChangePassword on the restored user leads to passwordChangeRequired',
      build: () => SessionCubit(authRepository),
      setUp: () {
        when(() => authRepository.refresh()).thenAnswer((_) async => 'new-token');
        when(() => authRepository.me()).thenAnswer((_) async => const AuthSuccess(_userMustChange));
      },
      act: (cubit) => cubit.restore(),
      expect: () => [const SessionPasswordChangeRequired(_userMustChange)],
    );

    blocTest<SessionCubit, SessionState>(
      'no valid refresh token leads to unauthenticated, never throwing',
      build: () => SessionCubit(authRepository),
      setUp: () {
        when(() => authRepository.refresh()).thenThrow(StateError('no refresh token'));
      },
      act: (cubit) => cubit.restore(),
      expect: () => [const SessionUnauthenticated()],
    );
  });

  group('login', () {
    blocTest<SessionCubit, SessionState>(
      'success goes authenticating -> authenticated',
      build: () => SessionCubit(authRepository),
      setUp: () {
        when(() => authRepository.login('admin', '12345678')).thenAnswer(
          (_) async => const AuthSuccess(
            LoginResult(accessToken: 'a', refreshToken: 'r', mustChangePassword: false),
          ),
        );
        when(() => authRepository.me()).thenAnswer((_) async => const AuthSuccess(_user));
      },
      act: (cubit) => cubit.login('admin', '12345678'),
      expect: () => [const SessionAuthenticating(), const SessionAuthenticated(_user)],
    );

    blocTest<SessionCubit, SessionState>(
      'invalid credentials goes authenticating -> unauthenticated',
      build: () => SessionCubit(authRepository),
      setUp: () {
        when(() => authRepository.login('admin', 'wrong'))
            .thenAnswer((_) async => const AuthError(InvalidCredentialsFailure()));
      },
      act: (cubit) => cubit.login('admin', 'wrong'),
      expect: () => [const SessionAuthenticating(), const SessionUnauthenticated()],
    );
  });

  group('passwordChanged', () {
    blocTest<SessionCubit, SessionState>(
      'moves passwordChangeRequired to authenticated without a new /me call',
      build: () => SessionCubit(authRepository),
      seed: () => const SessionPasswordChangeRequired(_userMustChange),
      act: (cubit) => cubit.passwordChanged(),
      expect: () => [isA<SessionAuthenticated>()],
      verify: (_) => verifyNever(() => authRepository.me()),
    );
  });

  group('logout', () {
    blocTest<SessionCubit, SessionState>(
      'always ends at unauthenticated',
      build: () => SessionCubit(authRepository),
      seed: () => SessionAuthenticated(_user),
      setUp: () => when(() => authRepository.logout()).thenAnswer((_) async {}),
      act: (cubit) => cubit.logout(),
      expect: () => [const SessionUnauthenticated()],
    );
  });
}
