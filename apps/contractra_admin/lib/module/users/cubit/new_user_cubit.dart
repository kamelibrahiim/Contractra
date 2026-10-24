import 'package:bloc/bloc.dart';

import '../../../core/network/failures.dart';
import '../data_layer/models/role_option.dart';
import '../data_layer/models/user_summary.dart';
import '../data_layer/repository/users_repository.dart';

part 'new_user_state.dart';

class NewUserCubit extends Cubit<NewUserState> {
  NewUserCubit(this._repository) : super(const NewUserState()) {
    _loadRoles();
  }

  final UsersRepository _repository;

  Future<void> _loadRoles() async {
    final result = await _repository.listRoles();
    switch (result) {
      case UsersSuccess(value: final roles):
        emit(state.copyWith(rolesStatus: RolesLoadStatus.loaded, availableRoles: roles));
      case UsersError():
        emit(state.copyWith(rolesStatus: RolesLoadStatus.error));
    }
  }

  void toggleRole(String code, bool selected) {
    final updated = {...state.selectedRoleCodes};
    selected ? updated.add(code) : updated.remove(code);
    emit(state.copyWith(selectedRoleCodes: updated, validationError: () => null));
  }

  Future<void> submit(String loginIdentifier) async {
    if (state.submitStatus == SubmitStatus.submitting) return;

    if (loginIdentifier.trim().isEmpty) {
      emit(state.copyWith(validationError: () => NewUserValidationReason.identifierEmpty));
      return;
    }
    // FR-AUTH-003, enforced on the client ahead of the server's own check.
    if (state.selectedRoleCodes.isEmpty) {
      emit(state.copyWith(validationError: () => NewUserValidationReason.noRoleSelected));
      return;
    }

    emit(state.copyWith(
      submitStatus: SubmitStatus.submitting,
      validationError: () => null,
      serverError: () => null,
    ));

    final result = await _repository.create(
      loginIdentifier: loginIdentifier.trim(),
      roleCodes: state.selectedRoleCodes.toList(),
    );

    switch (result) {
      case UsersSuccess(value: final user):
        emit(state.copyWith(submitStatus: SubmitStatus.success, createdUser: () => user));
      case UsersError(failure: final failure):
        emit(state.copyWith(submitStatus: SubmitStatus.idle, serverError: () => failure));
    }
  }
}
