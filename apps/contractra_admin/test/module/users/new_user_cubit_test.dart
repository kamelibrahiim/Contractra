import 'package:bloc_test/bloc_test.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

import 'package:contractra_admin/core/network/failures.dart';
import 'package:contractra_admin/module/users/cubit/new_user_cubit.dart';
import 'package:contractra_admin/module/users/data_layer/models/role_option.dart';
import 'package:contractra_admin/module/users/data_layer/models/user_summary.dart';
import 'package:contractra_admin/module/users/data_layer/repository/users_repository.dart';

class _MockUsersRepository extends Mock implements UsersRepository {}

const _roles = [
  RoleOption(code: 'Admin', displayName: 'Admin'),
  RoleOption(code: 'Engineer', displayName: 'Engineer'),
];

void main() {
  late _MockUsersRepository repository;

  setUp(() {
    repository = _MockUsersRepository();
    when(() => repository.listRoles()).thenAnswer((_) async => const UsersSuccess(_roles));
  });

  blocTest<NewUserCubit, NewUserState>(
    'loads the available roles on creation',
    build: () => NewUserCubit(repository),
    expect: () => [
      isA<NewUserState>().having((s) => s.rolesStatus, 'rolesStatus', RolesLoadStatus.loaded),
    ],
  );

  blocTest<NewUserCubit, NewUserState>(
    'toggling a role updates the selection',
    build: () => NewUserCubit(repository),
    act: (cubit) => cubit.toggleRole('Engineer', true),
    skip: 1,
    verify: (cubit) => expect(cubit.state.selectedRoleCodes, {'Engineer'}),
  );

  blocTest<NewUserCubit, NewUserState>(
    'submitting an empty identifier fails client-side, never calling the repository',
    build: () => NewUserCubit(repository),
    act: (cubit) => cubit.submit(''),
    skip: 1,
    verify: (cubit) {
      expect(cubit.state.validationError, NewUserValidationReason.identifierEmpty);
      verifyNever(() => repository.create(loginIdentifier: any(named: 'loginIdentifier'), roleCodes: any(named: 'roleCodes')));
    },
  );

  blocTest<NewUserCubit, NewUserState>(
    'submitting with no role selected fails client-side (FR-AUTH-003)',
    build: () => NewUserCubit(repository),
    act: (cubit) => cubit.submit('new.user'),
    skip: 1,
    verify: (cubit) => expect(cubit.state.validationError, NewUserValidationReason.noRoleSelected),
  );

  blocTest<NewUserCubit, NewUserState>(
    'a taken identifier is surfaced as a server error',
    build: () => NewUserCubit(repository),
    setUp: () => when(() => repository.create(loginIdentifier: 'admin', roleCodes: ['Engineer']))
        .thenAnswer((_) async => const UsersError(LoginIdentifierTakenFailure())),
    act: (cubit) {
      cubit.toggleRole('Engineer', true);
      cubit.submit('admin');
    },
    skip: 1,
    verify: (cubit) => expect(cubit.state.serverError, isA<LoginIdentifierTakenFailure>()),
  );

  blocTest<NewUserCubit, NewUserState>(
    'success stores the created user',
    build: () => NewUserCubit(repository),
    setUp: () => when(() => repository.create(loginIdentifier: 'new.user', roleCodes: ['Engineer']))
        .thenAnswer((_) async => const UsersSuccess(
              UserSummary(id: 'u-9', loginIdentifier: 'new.user', roles: ['Engineer'], status: UserAccountStatus.active),
            )),
    act: (cubit) {
      cubit.toggleRole('Engineer', true);
      cubit.submit('new.user');
    },
    skip: 1,
    verify: (cubit) {
      expect(cubit.state.submitStatus, SubmitStatus.success);
      expect(cubit.state.createdUser?.loginIdentifier, 'new.user');
    },
  );
}
