namespace ConnectionRevitCloud.Server.Middleware;

public class AdminIpOnlyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _allowedIp;

    public AdminIpOnlyMiddleware(RequestDelegate next, IConfiguration cfg)
    {
        _next = next;
        _allowedIp = cfg["Security:AdminAllowedIp"] ?? "10.10.0.100";
    }

    public async Task Invoke(HttpContext ctx)
    {
        var remoteIp = ctx.Connection.RemoteIpAddress?.ToString() ?? "";
        if (remoteIp != _allowedIp)
        {
            ctx.Response.StatusCode = 403;
            await ctx.Response.WriteAsync("Forbidden");
            return;
        }
        await _next(ctx);
    }
}
