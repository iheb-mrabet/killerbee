using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using KillerBee.Core;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KillerBee.Hosting;

public sealed record RuntimeSettings(bool Demo, string TransportKey)
{
    public static RuntimeSettings Read(WebApplicationBuilder builder)
    {
        bool demo = builder.Configuration.GetValue<bool>("KillerBee:Demo");
        if (demo && !builder.Environment.IsDevelopment())
            throw new InvalidOperationException("Demo authentication is only permitted in Development.");
        if (!demo && !OperatingSystem.IsWindows())
            throw new InvalidOperationException("The AD/SQL per-user deployment requires Windows. Use the documented loopback demo on Linux.");
        var key = builder.Configuration["KillerBee:TransportKey"] ?? "";
        if (System.Text.Encoding.UTF8.GetByteCount(key) < 32)
            throw new InvalidOperationException("Set KillerBee__TransportKey to a random secret of at least 32 bytes, outside source control.");
        var urls = builder.Configuration["urls"] ?? "";
        if (demo && (string.IsNullOrEmpty(urls) || urls.Split(';').Any(s => !Loopback(new Uri(s)))))
            throw new InvalidOperationException("The demo must bind explicitly to loopback URLs only.");
        return new(demo, key);
    }
    public static bool Loopback(Uri uri) => uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || (IPAddress.TryParse(uri.Host, out var ip) && IPAddress.IsLoopback(ip));
    public void ValidateDestination(Uri uri)
    {
        if (!uri.IsAbsoluteUri || (Demo ? !Loopback(uri) : uri.Scheme != "https"))
            throw new InvalidOperationException("Demo destinations must be loopback; AD deployment destinations must use HTTPS.");
    }
}

public static class AppSetup
{
    public static RuntimeSettings AddKillerBee(this WebApplicationBuilder builder, bool local = false)
    {
        var settings = RuntimeSettings.Read(builder);
        builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 1_048_576);
        builder.Services.AddSingleton(settings);
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton<NonceStore>();
        builder.Services.AddSingleton<SecureProxy>();
        builder.Services.AddAuthorization();
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);
        builder.Services.AddAntiforgery(o =>
        {
            o.HeaderName = "X-CSRF-TOKEN";
            o.Cookie.Name = "kb.csrf";
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.Cookie.SecurePolicy = settings.Demo ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        });
        if (settings.Demo && local)
        {
            builder.Services.AddAuthentication("DemoCookie").AddCookie("DemoCookie", o =>
            {
                o.Cookie.Name = "kb.session";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Strict;
                o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                o.ExpireTimeSpan = TimeSpan.FromMinutes(45);
                o.SlidingExpiration = false;
                o.Events = new CookieAuthenticationEvents
                {
                    OnRedirectToLogin = c => AuthFailure(c.HttpContext, 401),
                    OnRedirectToAccessDenied = c => AuthFailure(c.HttpContext, 403)
                };
            });
        }
        else if (settings.Demo)
            builder.Services.AddAuthentication("DemoRelay").AddScheme<AuthenticationSchemeOptions, DemoRelayHandler>("DemoRelay", _ => { });
        else
            builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();
        builder.Services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = 429;
            o.AddPolicy("requests", c => RateLimitPartition.GetFixedWindowLimiter(
                c.User.Identity?.Name ?? c.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 240, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
        return settings;
    }

    private static Task AuthFailure(HttpContext c, int status)
    {
        c.Response.StatusCode = status;
        return c.Response.WriteAsJsonAsync(ApiReply.Failure(status == 401 ? "AUTH_REQUIRED" : "ACCESS_DENIED",
            status == 401 ? "Authentification nécessaire." : "Accès refusé.", c.TraceIdentifier));
    }

    public static void UseKillerBee(this WebApplication app)
    {
        var settings = app.Services.GetRequiredService<RuntimeSettings>();
        app.Use(async (c, next) =>
        {
            c.TraceIdentifier = Guid.NewGuid().ToString("N");
            c.Response.Headers["X-Correlation-ID"] = c.TraceIdentifier;
            c.Response.Headers["X-Content-Type-Options"] = "nosniff";
            c.Response.Headers["Referrer-Policy"] = "no-referrer";
            c.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
            if (c.Request.Path.StartsWithSegments("/api") || c.Request.Path.StartsWithSegments("/auth"))
                c.Response.Headers.CacheControl = "no-store";
            try
            {
                if (settings.Demo && (c.Connection.RemoteIpAddress is not { } remote || !IPAddress.IsLoopback(remote)))
                    throw new BusinessException(403, "LOOPBACK_ONLY", "La démonstration est limitée à la machine locale.");
                if (!settings.Demo && !c.Request.IsHttps)
                    throw new BusinessException(400, "HTTPS_REQUIRED", "Une connexion HTTPS est nécessaire.");
                await next();
            }
            catch (BusinessException e)
            {
                if (c.Response.HasStarted) throw;
                c.Response.StatusCode = e.Status;
                await c.Response.WriteAsJsonAsync(ApiReply.Failure(e.Code, e.Message, c.TraceIdentifier, e.Errors));
            }
            catch (Exception e) when (e is System.Text.Json.JsonException or BadHttpRequestException or AntiforgeryValidationException)
            {
                if (c.Response.HasStarted) throw;
                c.Response.StatusCode = 400;
                await c.Response.WriteAsJsonAsync(ApiReply.Failure("REQUEST_INVALID", "Requête ou jeton de formulaire invalide.", c.TraceIdentifier));
            }
            catch (Exception e)
            {
                app.Logger.LogError(e, "Request failed; correlation {Correlation}", c.TraceIdentifier);
                if (c.Response.HasStarted) throw;
                c.Response.StatusCode = 500;
                await c.Response.WriteAsJsonAsync(ApiReply.Failure("INTERNAL_ERROR", "Le traitement a échoué. Consultez l’identifiant de suivi.", c.TraceIdentifier));
            }
        });
        if (!settings.Demo) app.UseHsts();
        app.UseAuthentication();
        app.Use(async (c, next) =>
        {
            if (!settings.Demo && c.User.Identity?.IsAuthenticated == true)
            {
                var roles = new ClaimsIdentity();
                foreach (var role in Roles.All)
                {
                    var group = app.Configuration[$"KillerBee:Groups:{role}"];
                    if (!string.IsNullOrWhiteSpace(group) && c.User.IsInRole(group)) roles.AddClaim(new(ClaimTypes.Role, role));
                }
                c.User.AddIdentity(roles);
            }
            await next();
        });
        app.UseRateLimiter();
        app.UseAuthorization();
        app.MapGet("/health", () => Results.Ok(new { status = "up" }));
    }
}
