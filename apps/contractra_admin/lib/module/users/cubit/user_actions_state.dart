part of 'user_actions_cubit.dart';

enum UserActionsStatus { loading, loaded, error }

enum UserActionStatus { idle, running, succeeded, failed }

class UserActionsState {
  const UserActionsState({
    this.status = UserActionsStatus.loading,
    this.user,
    this.actionStatus = UserActionStatus.idle,
    this.failure,
  });

  final UserActionsStatus status;
  final UserSummary? user;
  final UserActionStatus actionStatus;
  final Failure? failure;

  UserActionsState copyWith({
    UserActionsStatus? status,
    UserSummary? Function()? user,
    UserActionStatus? actionStatus,
    Failure? Function()? failure,
  }) {
    return UserActionsState(
      status: status ?? this.status,
      user: user != null ? user() : this.user,
      actionStatus: actionStatus ?? this.actionStatus,
      failure: failure != null ? failure() : this.failure,
    );
  }
}
