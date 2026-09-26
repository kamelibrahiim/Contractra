import 'package:bloc/bloc.dart';

import '../../../core/network/failures.dart';
import '../data_layer/models/user_summary.dart';
import '../data_layer/repository/users_repository.dart';

part 'user_actions_state.dart';

/// Loads one user by id and runs disable/reactivate/reset-password on
/// them. Confirmation happens in the UI before any method here is called
/// — this cubit performs the action, it never asks for consent itself.
class UserActionsCubit extends Cubit<UserActionsState> {
  UserActionsCubit(this._repository, this._userId) : super(const UserActionsState()) {
    _load();
  }

  final UsersRepository _repository;
  final String _userId;

  Future<void> _load() async {
    emit(state.copyWith(status: UserActionsStatus.loading));
    final result = await _repository.getById(_userId);
    switch (result) {
      case UsersSuccess(value: final user):
        emit(state.copyWith(status: UserActionsStatus.loaded, user: () => user));
      case UsersError(failure: final failure):
        emit(state.copyWith(status: UserActionsStatus.error, failure: () => failure));
    }
  }

  Future<void> disable() => _runAction(_repository.disable);

  Future<void> reactivate() => _runAction(_repository.reactivate);

  Future<void> resetPassword() => _runAction(_repository.resetPassword);

  Future<void> _runAction(Future<UsersResult<void>> Function(String id) action) async {
    emit(state.copyWith(actionStatus: UserActionStatus.running));
    final result = await action(_userId);
    switch (result) {
      case UsersSuccess():
        emit(state.copyWith(actionStatus: UserActionStatus.succeeded));
        await _load();
      case UsersError(failure: final failure):
        emit(state.copyWith(actionStatus: UserActionStatus.failed, failure: () => failure));
    }
  }
}
