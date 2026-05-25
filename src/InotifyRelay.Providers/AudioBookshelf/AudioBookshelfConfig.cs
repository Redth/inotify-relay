namespace InotifyRelay.Providers.AudioBookshelf;

public sealed class AudioBookshelfConfig
{
    public string BaseUrl { get; set; } = "http://audiobookshelf:13378";

    /// <summary>API token from Settings → Users → API Token in the Audiobookshelf UI.</summary>
    public string ApiToken { get; set; } = "";

    /// <summary>The library id to scan (find under Settings → Libraries).</summary>
    public string LibraryId { get; set; } = "";

    /// <summary>If true, append <c>?force=1</c> to skip the "unchanged since last scan" optimization.</summary>
    public bool Force { get; set; } = false;
}
