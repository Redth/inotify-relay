namespace InotifyRelay.Web.Services;

/// <summary>Thrown by services when a user-supplied name collides with an existing record.</summary>
public sealed class DuplicateNameException(string message) : Exception(message);
