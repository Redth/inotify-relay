using InotifyRelay.Data;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace InotifyRelay.Web.Infrastructure;

/// <summary>
/// Configures the "oidc" scheme from the AuthSettings row in the database. Re-applies
/// values to <see cref="OpenIdConnectOptions"/> whenever the auth settings change.
/// </summary>
public sealed class DynamicOidcSchemeProvider(IServiceScopeFactory scopes)
{
    public async Task ApplyAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.AuthSettings.FirstOrDefaultAsync(x => x.Id == 1, ct);
        if (row is null || !row.OidcEnabled) return;

        var optsCache = scope.ServiceProvider.GetRequiredService<IOptionsMonitorCache<OpenIdConnectOptions>>();
        // build a new options instance and inject it into the named-options cache
        optsCache.TryRemove("oidc");
        var opts = new OpenIdConnectOptions
        {
            Authority = row.OidcAuthority,
            ClientId = row.OidcClientId,
            ClientSecret = row.OidcClientSecret,
            ResponseType = "code",
            SaveTokens = true,
            GetClaimsFromUserInfoEndpoint = true,
            CallbackPath = "/signin-oidc",
            SignedOutCallbackPath = "/signout-callback-oidc",
        };
        foreach (var s in (row.OidcScopes ?? "openid profile email").Split(' ', StringSplitOptions.RemoveEmptyEntries))
            opts.Scope.Add(s);
        optsCache.TryAdd("oidc", opts);
    }
}
