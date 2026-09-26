part of 'session_cubit.dart';

sealed class SessionState {
  const SessionState();

  @override
  bool operator ==(Object other) => other.runtimeType == runtimeType;

  @override
  int get hashCode => runtimeType.hashCode;
}

/// Initial state, before splash has restored (or failed to restore) a
/// session. Router guards treat this as "not yet decided" and do not
/// redirect.
final class SessionUnknown extends SessionState {
  const SessionUnknown();
}

final class SessionUnauthenticated extends SessionState {
  const SessionUnauthenticated();
}

final class SessionAuthenticating extends SessionState {
  const SessionAuthenticating();
}

final class SessionAuthenticated extends SessionState {
  const SessionAuthenticated(this.user);
  final CurrentUser user;
}

final class SessionPasswordChangeRequired extends SessionState {
  const SessionPasswordChangeRequired(this.user);
  final CurrentUser user;
}
