using PalWorldService.Host.Services;

namespace PalWorldService.Host.Auth;

public class ServerSessionFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var serverId = http.Request.RouteValues["serverId"]?.ToString();
        if (string.IsNullOrWhiteSpace(serverId))
            return Results.BadRequest(new { error = "serverId required" });

        var sessions = http.RequestServices.GetRequiredService<SessionService>();
        var token = http.Request.Cookies[SessionService.CookieName]
            ?? http.Request.Headers["X-Pal-Session"].FirstOrDefault();

        if (!sessions.TryValidate(token, serverId, out _))
            return Results.Json(new { error = "Unauthorized. Login required." }, statusCode: StatusCodes.Status401Unauthorized);

        return await next(context);
    }
}

public static class AuthExtensions
{
    public static RouteGroupBuilder RequireServerSession(this RouteGroupBuilder group)
        => group.AddEndpointFilter<ServerSessionFilter>();

    public static RouteHandlerBuilder RequireServerSession(this RouteHandlerBuilder builder)
        => builder.AddEndpointFilter<ServerSessionFilter>();
}
