import 'dart:io';

import 'package:bloc_test/bloc_test.dart';
import 'package:flutter_test/flutter_test.dart';

import 'package:contractra_employee/module/auth/login/cubit/login_cubit.dart';

void main() {
  LoginCubit build() => LoginCubit(simulatedDelay: const Duration(milliseconds: 10));

  group('LoginCubit', () {
    test('starts idle', () => expect(build().state, const LoginIdle()));

    blocTest<LoginCubit, LoginState>(
      'valid input goes idle -> submitting -> idle',
      build: build,
      act: (cubit) => cubit.submit('admin', 'secret'),
      wait: const Duration(milliseconds: 50),
      expect: () => const [LoginSubmitting(), LoginIdle()],
    );

    blocTest<LoginCubit, LoginState>(
      'empty identifier emits validationError',
      build: build,
      act: (cubit) => cubit.submit('  ', 'secret'),
      expect: () => const [LoginValidationError()],
    );

    blocTest<LoginCubit, LoginState>(
      'empty password emits validationError',
      build: build,
      act: (cubit) => cubit.submit('admin', ''),
      expect: () => const [LoginValidationError()],
    );

    blocTest<LoginCubit, LoginState>(
      'a second submit while submitting is ignored',
      build: build,
      act: (cubit) {
        cubit.submit('admin', 'secret');
        cubit.submit('admin', 'secret');
      },
      wait: const Duration(milliseconds: 50),
      expect: () => const [LoginSubmitting(), LoginIdle()],
    );

    test('cubit files import no network package', () {
      for (final path in [
        'lib/module/auth/login/cubit/login_cubit.dart',
        'lib/module/auth/login/cubit/login_state.dart',
      ]) {
        final source = File(path).readAsStringSync();
        expect(source.contains('package:dio'), isFalse, reason: path);
        expect(source.contains('package:fast_dio'), isFalse, reason: path);
      }
    });
  });
}
