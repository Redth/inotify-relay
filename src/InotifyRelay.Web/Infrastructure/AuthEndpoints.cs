using InotifyRelay.Data.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace InotifyRelay.Web.Infrastructure;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/auth/login", async (
            [FromForm] string email,
            [FromForm] string password,
            [FromForm] bool rememberMe,
            [FromForm] string? returnUrl,
            SignInManager<ApplicationUser> signIn) =>
        {
            var result = await signIn.PasswordSignInAsync(email, password, rememberMe, lockoutOnFailure: false);
            if (result.Succeeded)
                return Results.LocalRedirect(string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl);
            return Results.LocalRedirect($"/login?error=1&returnUrl={Uri.EscapeDataString(returnUrl ?? "/")}");
        }).DisableAntiforgery();

        app.MapPost("/api/auth/logout", async (HttpContext ctx, SignInManager<ApplicationUser> signIn) =>
        {
            await signIn.SignOutAsync();
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.LocalRedirect("/login");
        }).DisableAntiforgery();

        app.MapGet("/api/auth/oidc", (HttpContext ctx, [FromQuery] string? returnUrl) =>
            Results.Challenge(new AuthenticationProperties
            {
                RedirectUri = string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl
            }, [OpenIdConnectDefaults.AuthenticationScheme]));

        app.MapPost("/api/auth/setup", async (
            [FromForm] string email,
            [FromForm] string password,
            UserManager<ApplicationUser> userMgr,
            SignInManager<ApplicationUser> signIn) =>
        {
            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
            };
            var create = await userMgr.CreateAsync(user, password);
            if (!create.Succeeded)
                return Results.LocalRedirect("/setup?error=" + Uri.EscapeDataString(string.Join("; ", create.Errors.Select(e => e.Description))));
            await userMgr.AddToRoleAsync(user, "Admin");
            await signIn.SignInAsync(user, isPersistent: true);
            return Results.LocalRedirect("/");
        }).DisableAntiforgery();

        return app;
    }
}
