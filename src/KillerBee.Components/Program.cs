using KillerBee.Core;
using KillerBee.Data;
using KillerBee.Hosting;
using Microsoft.EntityFrameworkCore;

// Schema generation is offline: no AD, live SQL connection or credentials are used.
if (args.Contains("--schema", StringComparer.Ordinal))
{
    var opts = new DbContextOptionsBuilder<KillerBeeDb>().UseSqlServer("Server=localhost;Database=KillerBee;Integrated Security=true;Encrypt=true").Options;
    using var db = new KillerBeeDb(opts);
    Console.WriteLine(db.Database.GenerateCreateScript());
    Console.WriteLine("GO\nIF SCHEMA_ID(N'SCH_PROD') IS NULL EXEC(N'CREATE SCHEMA [SCH_PROD] AUTHORIZATION [dbo]');\nGO\n" + KillerBeeDb.Views(false));
    return;
}

var builder = WebApplication.CreateBuilder(args);
var settings = builder.AddKillerBee();
var instance = builder.Configuration["KillerBee:Instance"] ?? "components";
var dbOptions = new DbContextOptionsBuilder<KillerBeeDb>();
if (settings.Demo)
{
    string path = builder.Configuration["KillerBee:DemoDatabase"] ?? throw new InvalidOperationException("Configure an absolute KillerBee__DemoDatabase path.");
    if (!Path.IsPathFullyQualified(path)) throw new InvalidOperationException("Demo database path must be absolute.");
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    dbOptions.UseSqlite(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = path, DefaultTimeout = 30, ForeignKeys = true }.ToString());
}
else
{
    var connection = builder.Configuration.GetConnectionString("KillerBee") ?? throw new InvalidOperationException("Set ConnectionStrings__KillerBee.");
    DataProxy.ValidateConnection(connection);
    dbOptions.UseSqlServer(connection);
}
var options = dbOptions.Options;
if (args.Contains("--init-demo", StringComparer.Ordinal))
{
    if (!settings.Demo) throw new InvalidOperationException("Demo initialization cannot run in AD mode.");
    await using var db = new KillerBeeDb(options);
    await Seed.Demo(db);
    return;
}
builder.Services.AddSingleton(new DataProxy(options, settings.Demo));
builder.Services.AddSingleton<IBusinessPlugin, CataloguePlugin>();
builder.Services.AddSingleton<IBusinessPlugin, ProcessPlugin>();
builder.Services.AddSingleton<IBusinessPlugin, TestPlugin>();
builder.Services.AddSingleton<IBusinessPlugin>(new ReportingPlugin(settings.Demo, instance));
builder.Services.AddSingleton<OperationEngine>();
var app = builder.Build();
app.UseKillerBee();
var cipher = new TeachingCipher(settings.TransportKey, "platform-components");
long total = 0, errors = 0, durationMs = 0;

// The transactional controller orchestrates plugins through the data proxy. Both VM instances run the same host.
app.MapPost("/components/execute", async (SealedMessage message, HttpContext context, NonceStore nonces, OperationEngine engine) =>
{
    var start = System.Diagnostics.Stopwatch.StartNew();
    RemoteReply response;
    string correlation = context.TraceIdentifier;
    try
    {
        var request = cipher.Open<DispatchRequest>(message, n => nonces.Accept("components:request", n));
        correlation = request.RequestId.ToString("N");
        var result = await engine.Execute(request, context.User);
        response = new(200, ApiReply.Success(result, correlation));
    }
    catch (BusinessException e)
    {
        Interlocked.Increment(ref errors);
        app.Logger.LogWarning("Operation rejected {Code}; request {Correlation}", e.Code, correlation);
        response = new(e.Status, ApiReply.Failure(e.Code, e.Message, correlation, e.Errors));
    }
    Interlocked.Increment(ref total);
    Interlocked.Add(ref durationMs, start.ElapsedMilliseconds);
    return Results.Json(cipher.Seal(response));
}).RequireAuthorization().RequireRateLimiting("requests");

app.MapGet("/metrics", () => Results.Ok(new { instance, requests = Interlocked.Read(ref total),
    errors = Interlocked.Read(ref errors), totalDurationMs = Interlocked.Read(ref durationMs) }))
    .RequireAuthorization(p => p.RequireRole(Roles.Research));
app.Run();
