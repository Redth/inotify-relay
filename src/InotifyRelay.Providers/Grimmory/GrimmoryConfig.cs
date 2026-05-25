namespace InotifyRelay.Providers.Grimmory;

public sealed class GrimmoryConfig
{
    public string BaseUrl { get; set; } = "http://grimmory:6060";

    /// <summary>Grimmory username. Required — Grimmory authenticates via username+password
    /// and exchanges them for a short-lived JWT.</summary>
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";

    /// <summary>Numeric library id. Use the "Fetch libraries" button in the UI to pick.</summary>
    public string LibraryId { get; set; } = "";
}
