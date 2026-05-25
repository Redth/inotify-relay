using System.Text.Json;
using InotifyRelay.Core.Providers;
using InotifyRelay.Web.Services;
using Microsoft.AspNetCore.Components;

namespace InotifyRelay.Web.Components.ProviderEditors;

/// <summary>
/// Base for typed provider-config editors. Handles the JSON ↔ typed model
/// round-trip + the "Fetch libraries" pattern used by Jellyfin / Plex /
/// Audiobookshelf.
/// </summary>
public abstract class ProviderEditorBase<TConfig> : ComponentBase
    where TConfig : class, new()
{
    [Parameter] public string Json { get; set; } = "{}";
    [Parameter] public EventCallback<string> JsonChanged { get; set; }

    [Inject] protected ProviderCatalog Catalog { get; set; } = default!;

    /// <summary>The typed config bound to form fields. Re-parsed only when
    /// the inbound Json differs from what this component last emitted.</summary>
    protected TConfig Config { get; set; } = new();

    private string _lastEmitted = "";
    /// <summary>The most recent JSON this component emitted. Subclasses use this
    /// in OnParametersSet to tell "outside change → resync local view-state" from
    /// "we just emitted this → don't resync, would clobber in-progress input".</summary>
    protected string LastEmittedJson => _lastEmitted;
    private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };

    /// <summary>The provider's TypeKey ("jellyfin", "plex", ...). Used to pull
    /// the right IRelayProviderWithLibraries from <see cref="Catalog"/>.</summary>
    protected abstract string ProviderKey { get; }

    protected List<LibraryInfo>? Libraries { get; private set; }
    protected string? LibraryError { get; private set; }
    protected bool LibraryLoading { get; private set; }

    protected override void OnParametersSet()
    {
        if (Json != _lastEmitted)
        {
            try { Config = JsonSerializer.Deserialize<TConfig>(Json, Opts) ?? new TConfig(); }
            catch { Config = new TConfig(); }
        }
    }

    /// <summary>Call from <c>@bind:after</c> handlers to push the current model back as JSON.</summary>
    protected async Task FlushAsync()
    {
        _lastEmitted = JsonFormat.Serialize(Config);
        await JsonChanged.InvokeAsync(_lastEmitted);
    }

    protected async Task FetchLibrariesAsync()
    {
        LibraryError = null;
        LibraryLoading = true;
        await InvokeAsync(StateHasChanged);
        try
        {
            // make sure any pending in-memory edits are saved into the JSON parent first
            await FlushAsync();
            if (Catalog.Get(ProviderKey) is IRelayProviderWithLibraries p)
            {
                var libs = await p.ListLibrariesAsync(JsonFormat.Serialize(Config), CancellationToken.None);
                Libraries = libs.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase).ToList();
            }
            else
            {
                LibraryError = $"Provider '{ProviderKey}' doesn't support library discovery.";
            }
        }
        catch (Exception ex)
        {
            LibraryError = ex.Message;
            Libraries = null;
        }
        finally
        {
            LibraryLoading = false;
        }
    }
}
