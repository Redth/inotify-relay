using InotifyRelay.Core.Pipeline;
using InotifyRelay.Data;
using InotifyRelay.Data.Entities;
using InotifyRelay.Providers;
using InotifyRelay.Watcher;
using InotifyRelay.Web.Components;
using InotifyRelay.Web.Infrastructure;
using InotifyRelay.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ---- Paths & data directory ----
var dataDir = builder.Configuration["InotifyRelay:DataDir"]
              ?? Environment.GetEnvironmentVariable("INOTIFY_RELAY_DATA")
              ?? Path.Combine(AppContext.BaseDirectory, "data");
Directory.CreateDirectory(dataDir);
Directory.CreateDirectory(Path.Combine(dataDir, "logs"));
var dbPath = Path.Combine(dataDir, "inotify-relay.db");

// ---- Serilog ----
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(Path.Combine(dataDir, "logs", "inotify-relay-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7));

// ---- Database ----
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlite($"Data Source={dbPath}"));

// ---- Identity ----
builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(opt =>
    {
        opt.Password.RequiredLength = 8;
        opt.Password.RequireNonAlphanumeric = false;
        opt.Password.RequireUppercase = false;
        opt.User.RequireUniqueEmail = true;
        opt.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(opt =>
{
    opt.LoginPath = "/login";
    opt.AccessDeniedPath = "/access-denied";
    opt.ExpireTimeSpan = TimeSpan.FromHours(8);
    opt.SlidingExpiration = true;
});

builder.Services.AddAuthorization(opt =>
{
    opt.AddPolicy("Admin", p => p.RequireRole("Admin"));
});

// ---- Pluggable OIDC (configured at runtime from DB) ----
builder.Services.AddSingleton<DynamicOidcSchemeProvider>();
builder.Services.AddAuthentication()
    .AddOpenIdConnect("oidc", options =>
    {
        // configured from DB at startup via DynamicOidcSchemeProvider; defaults below avoid null
        options.Authority = "https://example.invalid";
        options.ClientId = "placeholder";
        options.CallbackPath = "/signin-oidc";
        options.SignedOutCallbackPath = "/signout-callback-oidc";
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Scope.Add("email");
        options.SaveTokens = true;
        options.GetClaimsFromUserInfoEndpoint = true;
    });

// ---- Blazor ----
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();

// ---- App services ----
builder.Services.AddInotifyRelayProviders();
builder.Services.AddInotifyRelayWatcher();
builder.Services.AddSingleton<IConfigChangeNotifier, ConfigChangeNotifier>();
builder.Services.AddSingleton<DeliveryQueue>();
builder.Services.AddScoped<IConfigStore, EfConfigStore>();
builder.Services.AddScoped<RuleService>();
builder.Services.AddScoped<TargetService>();
builder.Services.AddSingleton<ProviderCatalog>();

// hosted services
builder.Services.AddHostedService<WatcherSyncService>();
builder.Services.AddHostedService<EventProcessorService>();
builder.Services.AddHostedService<DeliveryWorkerService>();

var app = builder.Build();

// ---- Migrate DB on startup ----
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();

    if (!db.AuthSettings.Any()) db.AuthSettings.Add(new AuthSettingsEntity { Id = 1 });
    if (!db.SystemSettings.Any()) db.SystemSettings.Add(new SystemSettingsEntity { Id = 1 });
    db.SaveChanges();

    // ensure roles exist
    var roleMgr = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    foreach (var r in new[] { "Admin", "Viewer" })
        if (!await roleMgr.RoleExistsAsync(r)) await roleMgr.CreateAsync(new IdentityRole(r));
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseStaticFiles();
app.MapStaticAssets();

app.UseRouting();
app.UseMiddleware<FirstRunRedirectMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// auth endpoints (cookie sign-in/out)
app.MapAuthEndpoints();

app.Run();
