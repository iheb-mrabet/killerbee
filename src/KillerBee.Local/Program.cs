using System.Security.Claims;
using KillerBee.Core;
using KillerBee.Hosting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;

var builder = WebApplication.CreateBuilder(args);
var settings = builder.AddKillerBee(local: true);
var platform = new Uri(builder.Configuration["KillerBee:PlatformUrl"] ?? "https://kb-platform.example.test:7443/platform/dispatch");
settings.ValidateDestination(platform);
var app = builder.Build();
app.UseKillerBee();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/session", (HttpContext context, IAntiforgery antiforgery) => Results.Json(new
{
    demo = settings.Demo,
    csrfToken = antiforgery.GetAndStoreTokens(context).RequestToken,
    user = context.User.Identity?.IsAuthenticated == true ? new
    {
        name = context.User.Identity.Name,
        roles = Roles.All.Where(context.User.IsInRole).ToArray()
    } : null
}));

app.MapGet("/auth/windows", () => Results.Ok(new { authenticated = true })).RequireAuthorization();

if (settings.Demo)
{
    app.MapPost("/auth/demo-login", async (DemoLogin input, HttpContext context, IAntiforgery antiforgery) =>
    {
        await antiforgery.ValidateRequestAsync(context);
        Validation.Require(Roles.All.Contains(input.Role), "Profil de démonstration inconnu.");
        var identity = new ClaimsIdentity([new(ClaimTypes.Name, $"demo.{input.Role.ToLowerInvariant()}"), new(ClaimTypes.Role, input.Role)], "DemoCookie");
        await context.SignInAsync("DemoCookie", new ClaimsPrincipal(identity));
        return Results.Ok(new { authenticated = true });
    }).RequireRateLimiting("requests");

    app.MapPost("/auth/logout", async (HttpContext context, IAntiforgery antiforgery) =>
    {
        await antiforgery.ValidateRequestAsync(context);
        await context.SignOutAsync("DemoCookie");
        return Results.Ok(new { authenticated = false });
    }).RequireAuthorization();
}

// Application-local middleware: validates the caller, normalizes the request and delegates all platform I/O to the proxy.
app.MapPost("/api/operations", async (DispatchRequest request, HttpContext context, IAntiforgery antiforgery, SecureProxy proxy) =>
{
    await antiforgery.ValidateRequestAsync(context);
    Operations.Authorize(request, context.User);
    var reply = await proxy.Dispatch(platform, "application-platform", request, context.User, context.RequestAborted);
    context.Response.Headers["X-Request-ID"] = request.RequestId.ToString();
    return Results.Json(reply.Reply, statusCode: reply.HttpStatus);
}).RequireAuthorization().RequireRateLimiting("requests");

app.MapFallbackToFile("index.html");
app.Run();
public sealed record DemoLogin(string Role);
