import 'dart:async';

import 'package:bloc/bloc.dart';

import '../../../core/network/failures.dart';
import '../data_layer/models/user_summary.dart';
import '../data_layer/repository/users_repository.dart';

part 'users_list_state.dart';

class UsersListCubit extends Cubit<UsersListState> {
  UsersListCubit(this._repository) : super(const UsersListState()) {
    _load();
  }

  final UsersRepository _repository;
  Timer? _debounce;

  void search(String value) {
    _debounce?.cancel();
    _debounce = Timer(const Duration(milliseconds: 300), () {
      emit(state.copyWith(search: value, page: 1));
      _load();
    });
  }

  void filterByStatus(UserAccountStatus? status) {
    emit(state.copyWith(statusFilter: () => status, page: 1));
    _load();
  }

  Future<void> goToPage(int page) async {
    if (page < 1 || page > state.totalPages || page == state.page) return;
    emit(state.copyWith(page: page));
    await _load();
  }

  Future<void> refresh() => _load();

  Future<void> _load() async {
    emit(state.copyWith(status: UsersListStatus.loading));

    final result = await _repository.list(
      page: state.page,
      pageSize: state.pageSize,
      search: state.search,
      status: state.statusFilter,
    );

    switch (result) {
      case UsersSuccess(value: final page):
        emit(state.copyWith(
          status: UsersListStatus.loaded,
          items: page.items,
          totalCount: page.totalCount,
        ));
      case UsersError(failure: final failure):
        emit(state.copyWith(status: UsersListStatus.error, failure: failure));
    }
  }

  @override
  Future<void> close() {
    _debounce?.cancel();
    return super.close();
  }
}
