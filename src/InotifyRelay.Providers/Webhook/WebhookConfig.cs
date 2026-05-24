namespace InotifyRelay.Providers.Webhook;

public sealed class WebhookConfig
{
    public string UrlTemplate { get; set; } = "https://example.com/webhook";
    public string Method { get; set; } = "POST";
    public Dictionary<string, string> Headers { get; set; } = new();
    public string? BodyTemplate { get; set; } = "{\"event\":\"{event}\",\"path\":\"{path|jsonescape}\"}";
    public string ContentType { get; set; } = "application/json";
    public WebhookAuth Auth { get; set; } = new();
}

public sealed class WebhookAuth
{
    public string Type { get; set; } = "none"; // none | bearer | basic
    public string? Token { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
}
