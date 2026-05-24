using InotifyRelay.Core.Events;

namespace InotifyRelay.Core.Providers;

public sealed record RelayContext(
    FileSystemChange Change,
    string RuleName,
    string TargetName,
    string ProviderConfigJson,
    string? TemplateOverrideJson);

public sealed record RelayResult(
    bool Success,
    int StatusCode,
    string? ResponseSnippet,
    string? Error,
    TimeSpan Elapsed)
{
    public static RelayResult Ok(int statusCode, string? body, TimeSpan elapsed)
        => new(true, statusCode, Truncate(body, 4096), null, elapsed);

    public static RelayResult Fail(int statusCode, string? body, string error, TimeSpan elapsed)
        => new(false, statusCode, Truncate(body, 4096), error, elapsed);

    private static string? Truncate(string? s, int max)
        => s is null ? null : s.Length <= max ? s : s[..max];
}

public interface IRelayProvider
{
    string TypeKey { get; }
    string DisplayName { get; }
    Type ConfigType { get; }
    object CreateDefaultConfig();
    Task<RelayResult> SendAsync(RelayContext ctx, CancellationToken ct);
}
