part of 'new_user_cubit.dart';

class NewUserState {
  const NewUserState({
    this.rolesStatus = RolesLoadStatus.loading,
    this.availableRoles = const [],
    this.selectedRoleCodes = const {},
    this.submitStatus = SubmitStatus.idle,
    this.validationError,
    this.serverError,
    this.createdUser,
  });

  final RolesLoadStatus rolesStatus;
  final List<RoleOption> availableRoles;
  final Set<String> selectedRoleCodes;
  final SubmitStatus submitStatus;

  /// FR-AUTH-003: at least one role is required. Non-null only after a
  /// submit attempt that failed this rule.
  final NewUserValidationReason? validationError;
  final Failure? serverError;
  final UserSummary? createdUser;

  NewUserState copyWith({
    RolesLoadStatus? rolesStatus,
    List<RoleOption>? availableRoles,
    Set<String>? selectedRoleCodes,
    SubmitStatus? submitStatus,
    NewUserValidationReason? Function()? validationError,
    Failure? Function()? serverError,
    UserSummary? Function()? createdUser,
  }) {
    return NewUserState(
      rolesStatus: rolesStatus ?? this.rolesStatus,
      availableRoles: availableRoles ?? this.availableRoles,
      selectedRoleCodes: selectedRoleCodes ?? this.selectedRoleCodes,
      submitStatus: submitStatus ?? this.submitStatus,
      validationError: validationError != null ? validationError() : this.validationError,
      serverError: serverError != null ? serverError() : this.serverError,
      createdUser: createdUser != null ? createdUser() : this.createdUser,
    );
  }
}

enum RolesLoadStatus { loading, loaded, error }

enum SubmitStatus { idle, submitting, success }

enum NewUserValidationReason { identifierEmpty, noRoleSelected }
