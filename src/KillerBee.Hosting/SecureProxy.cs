using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using KillerBee.Core;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KillerBee.Hosting;

public sealed class NonceStore(IMemoryCache cache)
{
    private readonly object gate = new();
    public void Accept(string scope, string nonce)
    {
        lock (gate)
        {
            var key = scope + ":" + nonce;
            if (cache.TryGetValue(key, out _)) throw new BusinessException(409, "REPLAY_REJECTED", "Ce message a déjà été traité.");
            cache.Set(key, true, TimeSpan.FromMinutes(5));
        }
    }
}

public sealed record RemoteReply(int HttpStatus, ApiReply Reply);
public sealed record RelayIdentity(string Name, string[] Roles, long ExpiresAt, string Nonce);

public sealed class DemoRelayHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder, RuntimeSettings settings, NonceStore nonces)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!settings.Demo) return AuthenticateResult.Fail("Demo disabled.");
        var encoded = Request.Headers["X-KB-Demo-Identity"].ToString();
        var signature = Request.Headers["X-KB-Demo-Signature"].ToString();
        if (string.IsNullOrEmpty(encoded) || encoded.Length > 2048) return AuthenticateResult.NoResult();
        try
        {
            Request.EnableBuffering();
            using var body = new MemoryStream();
            await Request.Body.CopyToAsync(body, Context.RequestAborted);
            Request.Body.Position = 0;
            var expected = DemoSignature(settings.TransportKey, encoded, Request.Path, body.ToArray());
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(signature), expected))
                return AuthenticateResult.Fail("Invalid relay signature.");
            var id = JsonSerializer.Deserialize<RelayIdentity>(Convert.FromBase64String(encoded), JsonDefaults.Options);
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (id is null || id.ExpiresAt < now || id.ExpiresAt > now + 90 || id.Roles.Any(r => !Core.Roles.All.Contains(r)))
                return AuthenticateResult.Fail("Invalid relay identity.");
            nonces.Accept("identity", id.Nonce);
            var claims = new List<Claim> { new(ClaimTypes.Name, id.Name) };
            claims.AddRange(id.Roles.Select(r => new Claim(ClaimTypes.Role, r)));
            return AuthenticateResult.Success(new(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name)), Scheme.Name));
        }
        catch (Exception e) when (e is FormatException or JsonException or BusinessException)
        {
            return AuthenticateResult.Fail("Invalid relay identity.");
        }
    }
    public static byte[] DemoSignature(string secret, string identity, string path, byte[] body)
        => HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(
            $"demo-relay\nPOST\n{path}\n{identity}\n{Convert.ToHexString(SHA256.HashData(body))}"));
}

public sealed class SecureProxy(RuntimeSettings settings, NonceStore nonces)
{
    public async Task<RemoteReply> Dispatch(Uri destination, string purpose, DispatchRequest request,
        ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        settings.ValidateDestination(destination);
        var cipher = new TeachingCipher(settings.TransportKey, purpose);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(cipher.Seal(request), JsonDefaults.Options);
        async Task<RemoteReply> Send()
        {
            // A handler is never shared between users: pooled authenticated HTTP connections can carry identities.
            using var handler = new HttpClientHandler { UseDefaultCredentials = !settings.Demo, AllowAutoRedirect = false };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
            using var message = new HttpRequestMessage(HttpMethod.Post, destination) { Content = new ByteArrayContent(bytes) };
            message.Content.Headers.ContentType = new("application/json");
            if (settings.Demo)
            {
                var identity = new RelayIdentity(user.Identity?.Name ?? "", Core.Roles.All.Where(user.IsInRole).ToArray(),
                    DateTimeOffset.UtcNow.AddSeconds(60).ToUnixTimeSeconds(), Guid.NewGuid().ToString("N"));
                var encoded = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(identity, JsonDefaults.Options));
                message.Headers.Add("X-KB-Demo-Identity", encoded);
                message.Headers.Add("X-KB-Demo-Signature", Convert.ToBase64String(
                    DemoRelayHandler.DemoSignature(settings.TransportKey, encoded, destination.AbsolutePath, bytes)));
            }
            using var response = await client.SendAsync(message, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new BusinessException(502, "UPSTREAM_AUTH_OR_TRANSPORT", "Le service distant a refusé la communication. Vérifiez ses journaux et la délégation AD.");
            var envelope = await response.Content.ReadFromJsonAsync<SealedMessage>(JsonDefaults.Options, cancellationToken)
                ?? throw new BusinessException(502, "UPSTREAM_INVALID", "Réponse distante invalide.");
            return cipher.Open<RemoteReply>(envelope, n => nonces.Accept(purpose + ":response", n));
        }
        try
        {
            if (settings.Demo) return await Send();
            if (!OperatingSystem.IsWindows()) throw new InvalidOperationException("Windows required.");
            var identity = user.Identities.OfType<WindowsIdentity>().FirstOrDefault()
                ?? throw new BusinessException(401, "WINDOWS_IDENTITY_REQUIRED", "Une identité Windows déléguable est nécessaire.");
            if (identity.ImpersonationLevel != TokenImpersonationLevel.Delegation)
                throw new BusinessException(403, "DELEGATION_REQUIRED", "La session Windows ne permet pas la délégation. Vérifiez Kerberos et les SPN.");
            return await WindowsIdentity.RunImpersonatedAsync(identity.AccessToken, Send);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            throw new BusinessException(503, "SERVICE_UNAVAILABLE", "Un service est indisponible. Réessayez plus tard.");
        }
    }
}
