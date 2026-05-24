namespace InotifyRelay.Providers.Jellyfin;

public sealed class JellyfinConfig
{
    public string BaseUrl { get; set; } = "http://jellyfin:8096";
    public string ApiKey { get; set; } = "";
    /// <summary>"refresh-library" or "report-path".</summary>
    public string Action { get; set; } = "refresh-library";

    /// <summary>Empty/null = all libraries; otherwise the Jellyfin library item id.</summary>
    public string? LibraryId { get; set; }

    /// <summary>Path template sent to /Library/Media/Updated when Action == report-path.</summary>
    public string PathTemplate { get; set; } = "{path}";

    public string UpdateType { get; set; } = "Modified"; // Created, Modified, Deleted
}
