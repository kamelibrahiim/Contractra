import 'package:bloc_test/bloc_test.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

import 'package:contractra_admin/core/network/failures.dart';
import 'package:contractra_admin/module/users/cubit/user_actions_cubit.dart';
import 'package:contractra_admin/module/users/data_layer/models/user_summary.dart';
import 'package:contractra_admin/module/users/data_layer/repository/users_repository.dart';

class _MockUsersRepository extends Mock implements UsersRepository {}

const _activeUser = UserSummary(id: 'u-1', loginIdentifier: 'admin', roles: ['Admin'], status: UserAccountStatus.active);
const _disabledUser = UserSummary(id: 'u-1', loginIdentifier: 'admin', roles: ['Admin'], status: UserAccountStatus.disabled);

void main() {
  late _MockUsersRepository repository;

  setUp(() => repository = _MockUsersRepository());

  blocTest<UserActionsCubit, UserActionsState>(
    'loads the user on creation',
    setUp: () => when(() => repository.getById('u-1')).thenAnswer((_) async => const UsersSuccess(_activeUser)),
    build: () => UserActionsCubit(repository, 'u-1'),
    expect: () => [
      isA<UserActionsState>().having((s) => s.status, 'status', UserActionsStatus.loaded),
    ],
  );

  blocTest<UserActionsCubit, UserActionsState>(
    'disable succeeds and reloads the user as disabled',
    setUp: () {
      when(() => repository.getById('u-1')).thenAnswer((_) async => const UsersSuccess(_activeUser));
      when(() => repository.disable('u-1')).thenAnswer((_) async {
        when(() => repository.getById('u-1')).thenAnswer((_) async => const UsersSuccess(_disabledUser));
        return const UsersSuccess(null);
      });
    },
    build: () => UserActionsCubit(repository, 'u-1'),
    act: (cubit) => cubit.disable(),
    skip: 1,
    verify: (cubit) {
      expect(cubit.state.actionStatus, UserActionStatus.succeeded);
      expect(cubit.state.user?.status, UserAccountStatus.disabled);
    },
  );

  blocTest<UserActionsCubit, UserActionsState>(
    'reactivate succeeds',
    setUp: () {
      when(() => repository.getById('u-1')).thenAnswer((_) async => const UsersSuccess(_disabledUser));
      when(() => repository.reactivate('u-1')).thenAnswer((_) async {
        when(() => repository.getById('u-1')).thenAnswer((_) async => const UsersSuccess(_activeUser));
        return const UsersSuccess(null);
      });
    },
    build: () => UserActionsCubit(repository, 'u-1'),
    act: (cubit) => cubit.reactivate(),
    skip: 1,
    verify: (cubit) => expect(cubit.state.user?.status, UserAccountStatus.active),
  );

  blocTest<UserActionsCubit, UserActionsState>(
    'resetPassword succeeds',
    setUp: () {
      when(() => repository.getById('u-1')).thenAnswer((_) async => const UsersSuccess(_activeUser));
      when(() => repository.resetPassword('u-1')).thenAnswer((_) async => const UsersSuccess(null));
    },
    build: () => UserActionsCubit(repository, 'u-1'),
    act: (cubit) => cubit.resetPassword(),
    skip: 1,
    verify: (cubit) => expect(cubit.state.actionStatus, UserActionStatus.succeeded),
  );

  blocTest<UserActionsCubit, UserActionsState>(
    'a failed action reports the failure without changing the loaded user',
    setUp: () {
      when(() => repository.getById('u-1')).thenAnswer((_) async => const UsersSuccess(_activeUser));
      when(() => repository.disable('u-1')).thenAnswer((_) async => const UsersError(NetworkFailure()));
    },
    build: () => UserActionsCubit(repository, 'u-1'),
    act: (cubit) => cubit.disable(),
    skip: 1,
    verify: (cubit) {
      expect(cubit.state.actionStatus, UserActionStatus.failed);
      expect(cubit.state.user?.status, UserAccountStatus.active);
    },
  );
}
