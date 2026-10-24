import 'package:bloc_test/bloc_test.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

import 'package:contractra_employee/core/network/failures.dart';
import 'package:contractra_employee/core/session/session_cubit.dart';
import 'package:contractra_employee/module/auth/change_password/cubit/change_password_cubit.dart';
import 'package:contractra_employee/module/auth/data_layer/models/current_user.dart';
import 'package:contractra_employee/module/auth/data_layer/repository/auth_repository.dart';

class _MockAuthRepository extends Mock implements AuthRepository {}

const _userMustChange = CurrentUser(
  id: 'u1',
  loginIdentifier: 'admin',
  roles: ['Admin'],
  permissions: ['Users.Manage'],
  mustChangePassword: true,
);

void main() {
  late _MockAuthRepository authRepository;
  late SessionCubit sessionCubit;

  setUp(() {
    authRepository = _MockAuthRepository();
    sessionCubit = SessionCubit(authRepository)
      ..emit(const SessionPasswordChangeRequired(_userMustChange));
  });

  group('client-side validation', () {
    blocTest<ChangePasswordCubit, ChangePasswordState>(
      'rejects a new password shorter than 8 characters, never calling the repository',
      build: () => ChangePasswordCubit(authRepository, sessionCubit),
      act: (cubit) => cubit.submit(
        currentPassword: '12345678',
        newPassword: 'short1',
        confirmPassword: 'short1',
      ),
      expect: () => [
        const ChangePasswordValidationError(ChangePasswordValidationReason.tooShort),
      ],
      verify: (_) => verifyNever(
        () => authRepository.changePassword(currentPassword: any(named: 'currentPassword'), newPassword: any(named: 'newPassword')),
      ),
    );

    blocTest<ChangePasswordCubit, ChangePasswordState>(
      'rejects the default password as the new password',
      build: () => ChangePasswordCubit(authRepository, sessionCubit),
      act: (cubit) => cubit.submit(
        currentPassword: '12345678',
        newPassword: '12345678',
        confirmPassword: '12345678',
      ),
      expect: () => [
        const ChangePasswordValidationError(ChangePasswordValidationReason.isDefaultPassword),
      ],
    );

    blocTest<ChangePasswordCubit, ChangePasswordState>(
      'rejects a confirm that does not match the new password',
      build: () => ChangePasswordCubit(authRepository, sessionCubit),
      act: (cubit) => cubit.submit(
        currentPassword: '12345678',
        newPassword: 'NewPass#2026',
        confirmPassword: 'Different#2026',
      ),
      expect: () => [
        const ChangePasswordValidationError(ChangePasswordValidationReason.doesNotMatch),
      ],
    );
  });

  group('server outcome', () {
    blocTest<ChangePasswordCubit, ChangePasswordState>(
      'a server error is reported without touching the session',
      build: () => ChangePasswordCubit(authRepository, sessionCubit),
      setUp: () => when(() => authRepository.changePassword(
            currentPassword: 'wrong',
            newPassword: 'NewPass#2026',
          )).thenAnswer((_) async => const AuthError(InvalidCredentialsFailure())),
      act: (cubit) => cubit.submit(
        currentPassword: 'wrong',
        newPassword: 'NewPass#2026',
        confirmPassword: 'NewPass#2026',
      ),
      expect: () => [
        const ChangePasswordSubmitting(),
        const ChangePasswordServerError(InvalidCredentialsFailure()),
      ],
      verify: (_) => expect(sessionCubit.state, isA<SessionPasswordChangeRequired>()),
    );

    blocTest<ChangePasswordCubit, ChangePasswordState>(
      'success moves the session to authenticated',
      build: () => ChangePasswordCubit(authRepository, sessionCubit),
      setUp: () => when(() => authRepository.changePassword(
            currentPassword: '12345678',
            newPassword: 'NewPass#2026',
          )).thenAnswer((_) async => const AuthSuccess(null)),
      act: (cubit) => cubit.submit(
        currentPassword: '12345678',
        newPassword: 'NewPass#2026',
        confirmPassword: 'NewPass#2026',
      ),
      expect: () => [const ChangePasswordSubmitting(), const ChangePasswordSuccess()],
      verify: (_) => expect(sessionCubit.state, isA<SessionAuthenticated>()),
    );
  });
}
