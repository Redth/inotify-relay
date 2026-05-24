namespace InotifyRelay.Providers.Plex;

public sealed class PlexConfig
{
    public string BaseUrl { get; set; } = "http://plex:32400";
    public string Token { get; set; } = "";

    /// <summary>"refresh-section" or "refresh-path".</summary>
    public string Action { get; set; } = "refresh-section";

    /// <summary>Plex library section id (integer as string).</summary>
    public string SectionId { get; set; } = "1";

    /// <summary>Optional path template used for partial scans (sent as ?path=...).</summary>
    public string PathTemplate { get; set; } = "{directory}";
}
