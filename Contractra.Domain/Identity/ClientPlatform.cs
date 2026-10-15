namespace Contractra.Domain.Identity;

/// <summary>
/// Where the session's refresh token is delivered (auth contract v1.5 §5.1,
/// owner decision R38): read at login only and stored on the session. A
/// transport hint, never used for authorization.
/// </summary>
public enum ClientPlatform
{
    Web,
    Mobile
}
