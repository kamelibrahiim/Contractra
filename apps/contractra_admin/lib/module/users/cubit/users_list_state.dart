part of 'users_list_cubit.dart';

class UsersListState {
  const UsersListState({
    this.status = UsersListStatus.loading,
    this.items = const [],
    this.totalCount = 0,
    this.page = 1,
    this.pageSize = 20,
    this.search = '',
    this.statusFilter,
    this.failure,
  });

  final UsersListStatus status;
  final List<UserSummary> items;
  final int totalCount;
  final int page;
  final int pageSize;
  final String search;
  final UserAccountStatus? statusFilter;
  final Failure? failure;

  int get totalPages => totalCount == 0 ? 1 : ((totalCount - 1) ~/ pageSize) + 1;
  bool get isEmpty => status == UsersListStatus.loaded && items.isEmpty;

  UsersListState copyWith({
    UsersListStatus? status,
    List<UserSummary>? items,
    int? totalCount,
    int? page,
    int? pageSize,
    String? search,
    UserAccountStatus? Function()? statusFilter,
    Failure? failure,
  }) {
    return UsersListState(
      status: status ?? this.status,
      items: items ?? this.items,
      totalCount: totalCount ?? this.totalCount,
      page: page ?? this.page,
      pageSize: pageSize ?? this.pageSize,
      search: search ?? this.search,
      statusFilter: statusFilter != null ? statusFilter() : this.statusFilter,
      failure: failure,
    );
  }
}

enum UsersListStatus { loading, loaded, error }
