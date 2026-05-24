using InotifyRelay.Data;
using Microsoft.EntityFrameworkCore;

namespace InotifyRelay.Web.Infrastructure;

public sealed class FirstRunRedirectMiddleware(RequestDelegate next)
{
    private volatile bool _setupComplete;

    public async Task Invoke(HttpContext ctx, AppDbContext db)
    {
        if (!_setupComplete)
        {
            _setupComplete = await db.Users.AnyAsync();
        }

        var p = ctx.Request.Path.Value ?? "";
        var isSetup = p.StartsWith("/setup", StringComparison.OrdinalIgnoreCase)
                   || p.StartsWith("/api/auth/", StringComparison.OrdinalIgnoreCase)
                   || p.StartsWith("/_framework", StringComparison.OrdinalIgnoreCase)
                   || p.StartsWith("/_blazor", StringComparison.OrdinalIgnoreCase)
                   || p.StartsWith("/_content", StringComparison.OrdinalIgnoreCase)
                   || p.StartsWith("/css", StringComparison.OrdinalIgnoreCase)
                   || p.StartsWith("/js", StringComparison.OrdinalIgnoreCase)
                   || p.StartsWith("/lib", StringComparison.OrdinalIgnoreCase)
                   || p.StartsWith("/favicon", StringComparison.OrdinalIgnoreCase)
                   || p.EndsWith(".css", StringComparison.OrdinalIgnoreCase)
                   || p.EndsWith(".js", StringComparison.OrdinalIgnoreCase);

        if (!_setupComplete && !isSetup)
        {
            ctx.Response.Redirect("/setup");
            return;
        }
        if (_setupComplete && p.Equals("/setup", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Response.Redirect("/");
            return;
        }
        await next(ctx);
    }
}
