import 'package:bloc_test/bloc_test.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

import 'package:contractra_employee/core/network/failures.dart';
import 'package:contractra_employee/core/session/session_cubit.dart';
import 'package:contractra_employee/module/auth/data_layer/models/current_user.dart';
import 'package:contractra_employee/module/auth/data_layer/models/login_result.dart';
import 'package:contractra_employee/module/auth/data_layer/repository/auth_repository.dart';
import 'package:contractra_employee/module/auth/login/cubit/login_cubit.dart';

class _MockAuthRepository extends Mock implements AuthRepository {}

const _user = CurrentUser(
  id: 'u1',
  loginIdentifier: 'admin',
  roles: ['Admin'],
  permissions: ['Users.Manage'],
  mustChangePassword: false,
);

void main() {
  late _MockAuthRepository authRepository;
  late SessionCubit sessionCubit;

  setUp(() {
    authRepository = _MockAuthRepository();
    sessionCubit = SessionCubit(authRepository);
  });

  test('starts idle', () {
    expect(LoginCubit(sessionCubit).state, const LoginIdle());
  });

  blocTest<LoginCubit, LoginState>(
    'valid credentials go idle -> submitting -> succeeded',
    build: () => LoginCubit(sessionCubit),
    setUp: () {
      when(() => authRepository.login('admin', 'Secret#2026')).thenAnswer(
        (_) async => const AuthSuccess(LoginResult(accessToken: 'a', refreshToken: 'r', mustChangePassword: false)),
      );
      when(() => authRepository.me()).thenAnswer((_) async => const AuthSuccess(_user));
    },
    act: (cubit) => cubit.submit('admin', 'Secret#2026'),
    expect: () => [const LoginSubmitting(), const LoginSucceeded()],
  );

  blocTest<LoginCubit, LoginState>(
    'empty identifier emits validationError without calling the repository',
    build: () => LoginCubit(sessionCubit),
    act: (cubit) => cubit.submit('  ', 'Secret#2026'),
    expect: () => [const LoginValidationError()],
    verify: (_) => verifyNever(() => authRepository.login(any(), any())),
  );

  blocTest<LoginCubit, LoginState>(
    'empty password emits validationError',
    build: () => LoginCubit(sessionCubit),
    act: (cubit) => cubit.submit('admin', ''),
    expect: () => [const LoginValidationError()],
  );

  blocTest<LoginCubit, LoginState>(
    'invalid credentials go idle -> submitting -> invalidCredentials',
    build: () => LoginCubit(sessionCubit),
    setUp: () => when(() => authRepository.login('admin', 'wrong'))
        .thenAnswer((_) async => const AuthError(InvalidCredentialsFailure())),
    act: (cubit) => cubit.submit('admin', 'wrong'),
    expect: () => [const LoginSubmitting(), const LoginInvalidCredentials()],
  );

  blocTest<LoginCubit, LoginState>(
    'a disabled account is reported distinctly from invalid credentials',
    build: () => LoginCubit(sessionCubit),
    setUp: () => when(() => authRepository.login('disabled.user', 'x'))
        .thenAnswer((_) async => const AuthError(UserDisabledFailure())),
    act: (cubit) => cubit.submit('disabled.user', 'x'),
    expect: () => [const LoginSubmitting(), const LoginAccountDisabled()],
  );

  blocTest<LoginCubit, LoginState>(
    'a second submit while submitting is ignored',
    build: () => LoginCubit(sessionCubit),
    setUp: () {
      when(() => authRepository.login('admin', 'Secret#2026')).thenAnswer(
        (_) => Future.delayed(
          const Duration(milliseconds: 20),
          () => const AuthSuccess(LoginResult(accessToken: 'a', refreshToken: 'r', mustChangePassword: false)),
        ),
      );
      when(() => authRepository.me()).thenAnswer((_) async => const AuthSuccess(_user));
    },
    act: (cubit) {
      cubit.submit('admin', 'Secret#2026');
      cubit.submit('admin', 'Secret#2026');
    },
    wait: const Duration(milliseconds: 50),
    expect: () => [const LoginSubmitting(), const LoginSucceeded()],
    verify: (_) => verify(() => authRepository.login('admin', 'Secret#2026')).called(1),
  );
}
