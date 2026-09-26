import 'package:bloc_test/bloc_test.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mocktail/mocktail.dart';

import 'package:contractra_admin/core/network/failures.dart';
import 'package:contractra_admin/module/users/cubit/users_list_cubit.dart';
import 'package:contractra_admin/module/users/data_layer/models/user_page.dart';
import 'package:contractra_admin/module/users/data_layer/models/user_summary.dart';
import 'package:contractra_admin/module/users/data_layer/repository/users_repository.dart';

class _MockUsersRepository extends Mock implements UsersRepository {}

const _page = UserPage(
  items: [
    UserSummary(id: 'u-1', loginIdentifier: 'admin', roles: ['Admin'], status: UserAccountStatus.active),
  ],
  totalCount: 1,
);

const _emptyPage = UserPage(items: [], totalCount: 0);

void main() {
  late _MockUsersRepository repository;

  setUp(() => repository = _MockUsersRepository());

  blocTest<UsersListCubit, UsersListState>(
    'loads the first page on creation',
    setUp: () => when(() => repository.list(
          page: 1,
          pageSize: 20,
          search: '',
          status: null,
        )).thenAnswer((_) async => const UsersSuccess(_page)),
    build: () => UsersListCubit(repository),
    expect: () => [
      isA<UsersListState>().having((s) => s.status, 'status', UsersListStatus.loaded),
    ],
  );

  blocTest<UsersListCubit, UsersListState>(
    'an empty page is reported via isEmpty',
    setUp: () => when(() => repository.list(page: 1, pageSize: 20, search: '', status: null))
        .thenAnswer((_) async => const UsersSuccess(_emptyPage)),
    build: () => UsersListCubit(repository),
    verify: (cubit) => expect(cubit.state.isEmpty, isTrue),
  );

  blocTest<UsersListCubit, UsersListState>(
    'a repository error is surfaced',
    setUp: () => when(() => repository.list(page: 1, pageSize: 20, search: '', status: null))
        .thenAnswer((_) async => const UsersError(NetworkFailure())),
    build: () => UsersListCubit(repository),
    verify: (cubit) => expect(cubit.state.status, UsersListStatus.error),
  );

  blocTest<UsersListCubit, UsersListState>(
    'filterByStatus resets to page 1 and re-queries with the filter',
    setUp: () {
      when(() => repository.list(page: 1, pageSize: 20, search: '', status: null))
          .thenAnswer((_) async => const UsersSuccess(_page));
      when(() => repository.list(page: 1, pageSize: 20, search: '', status: UserAccountStatus.disabled))
          .thenAnswer((_) async => const UsersSuccess(_emptyPage));
    },
    build: () => UsersListCubit(repository),
    act: (cubit) => cubit.filterByStatus(UserAccountStatus.disabled),
    verify: (cubit) {
      expect(cubit.state.statusFilter, UserAccountStatus.disabled);
      verify(() => repository.list(page: 1, pageSize: 20, search: '', status: UserAccountStatus.disabled))
          .called(1);
    },
  );

  blocTest<UsersListCubit, UsersListState>(
    'goToPage re-queries with the new page and ignores an out-of-range page',
    setUp: () {
      when(() => repository.list(page: 1, pageSize: 20, search: '', status: null))
          .thenAnswer((_) async => UsersSuccess(UserPage(items: _page.items, totalCount: 25)));
      when(() => repository.list(page: 2, pageSize: 20, search: '', status: null))
          .thenAnswer((_) async => const UsersSuccess(UserPage(items: [], totalCount: 25)));
    },
    build: () => UsersListCubit(repository),
    act: (cubit) async {
      // Let the constructor's initial load resolve before paging.
      await Future<void>.delayed(const Duration(milliseconds: 10));
      await cubit.goToPage(2);
      cubit.goToPage(999);
    },
    wait: const Duration(milliseconds: 10),
    verify: (cubit) {
      expect(cubit.state.page, 2);
      verifyNever(() => repository.list(page: 999, pageSize: 20, search: '', status: null));
    },
  );

  blocTest<UsersListCubit, UsersListState>(
    'search debounces and resets to page 1',
    setUp: () {
      when(() => repository.list(page: 1, pageSize: 20, search: '', status: null))
          .thenAnswer((_) async => const UsersSuccess(_page));
      when(() => repository.list(page: 1, pageSize: 20, search: 'eng', status: null))
          .thenAnswer((_) async => const UsersSuccess(_emptyPage));
    },
    build: () => UsersListCubit(repository),
    act: (cubit) => cubit.search('eng'),
    wait: const Duration(milliseconds: 400),
    verify: (cubit) {
      expect(cubit.state.search, 'eng');
      verify(() => repository.list(page: 1, pageSize: 20, search: 'eng', status: null)).called(1);
    },
  );
}
