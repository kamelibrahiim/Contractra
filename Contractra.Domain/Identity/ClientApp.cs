namespace Contractra.Domain.Identity;

/// <summary>
/// The client app a session was opened for (auth contract v1.5 §4): set by the
/// server at login after the app-access check, fixed for the session.
/// </summary>
public enum ClientApp
{
    Admin,
    Employee
}
