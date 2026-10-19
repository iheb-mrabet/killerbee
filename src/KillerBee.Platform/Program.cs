using KillerBee.Core;
using KillerBee.Hosting;

var builder = WebApplication.CreateBuilder(args);
var settings = builder.AddKillerBee();
var destinations = builder.Configuration.GetSection("KillerBee:Components").Get<string[]>()
    ?? throw new InvalidOperationException("Configure at least two component destinations in KillerBee__Components__0 and __1.");
if (destinations.Length < 2) throw new InvalidOperationException("Two component instances are required by the project.");
var nodes = destinations.Select(s => new Uri(s)).ToArray();
foreach (var node in nodes) settings.ValidateDestination(node);
var app = builder.Build();
app.UseKillerBee();
int counter = -1;
long successes = 0, failures = 0;
var cipher = new TeachingCipher(settings.TransportKey, "application-platform");

// Single platform endpoint. Resolution happens before any component invocation.
app.MapPost("/platform/dispatch", async (SealedMessage message, HttpContext context, SecureProxy proxy, NonceStore nonces) =>
{
    RemoteReply response;
    string correlation = context.TraceIdentifier;
    try
    {
        var request = cipher.Open<DispatchRequest>(message, n => nonces.Accept("platform:request", n));
        correlation = request.RequestId.ToString("N");
        Operations.Authorize(request, context.User);
        int index = (int)((uint)Interlocked.Increment(ref counter) % nodes.Length);
        bool readOnly = request.Operation.EndsWith(".list", StringComparison.Ordinal) || request.Operation.EndsWith(".get", StringComparison.Ordinal)
            || request.Operation is "dashboard.read" or "system.identity";
        response = await Send(index);
        Interlocked.Increment(ref successes);

        async Task<RemoteReply> Send(int preferred)
        {
            BusinessException? last = null;
            for (int attempt = 0; attempt < (readOnly ? nodes.Length : 1); attempt++)
            {
                try { return await proxy.Dispatch(nodes[(preferred + attempt) % nodes.Length], "platform-components", request, context.User, context.RequestAborted); }
                catch (BusinessException e) when (e.Code is "SERVICE_UNAVAILABLE" or "UPSTREAM_AUTH_OR_TRANSPORT") { last = e; }
            }
            throw last ?? new BusinessException(503, "SERVICE_UNAVAILABLE", "Aucun composant disponible.");
        }
    }
    catch (BusinessException e)
    {
        Interlocked.Increment(ref failures);
        app.Logger.LogWarning("Dispatch rejected {Code}; request {Correlation}", e.Code, correlation);
        response = new(e.Status, ApiReply.Failure(e.Code, e.Message, correlation, e.Errors));
    }
    return Results.Json(cipher.Seal(response));
}).RequireAuthorization().RequireRateLimiting("requests");

app.MapGet("/metrics", () => Results.Ok(new { successes = Interlocked.Read(ref successes), failures = Interlocked.Read(ref failures), destinations = nodes.Length }))
    .RequireAuthorization(p => p.RequireRole(Roles.Research));
app.Run();
