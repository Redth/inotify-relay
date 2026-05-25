namespace InotifyRelay.Core.Providers;

/// <summary>A library, section, or whatever the target server calls its scannable units.</summary>
public sealed record LibraryInfo(string Id, string Name, string? Kind = null);

/// <summary>
/// Optional capability — implement on a provider that can enumerate the
/// scannable libraries / sections of the upstream service so the admin UI can
/// offer a dropdown instead of asking the user to find ids by hand.
/// </summary>
public interface IRelayProviderWithLibraries
{
    Task<IReadOnlyList<LibraryInfo>> ListLibrariesAsync(string configJson, CancellationToken ct);
}
