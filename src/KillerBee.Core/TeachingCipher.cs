using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KillerBee.Core;

public sealed record SealedMessage(int Version, string Nonce, long IssuedAt, string Payload, string Tag);

/// <summary>
/// Classroom cipher: affine byte substitution, key-derived substitution table, columnar transposition.
/// The classical transformations are deliberately NOT presented as modern cryptographic protection.
/// TLS is mandatory outside the loopback demo; HMAC adds integrity, not confidentiality.
/// </summary>
public sealed class TeachingCipher
{
    private readonly byte[] key;
    private readonly byte[] substitution = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
    private readonly byte[] inverse = new byte[256];
    private readonly int[] columns;

    public TeachingCipher(string secret, string purpose)
    {
        if (Encoding.UTF8.GetByteCount(secret) < 32) throw new ArgumentException("Transport key must contain at least 32 bytes.");
        key = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(purpose));
        for (int i = 255; i > 0; i--)
        {
            var random = HMACSHA256.HashData(key, BitConverter.GetBytes(i));
            var j = (int)(System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(random) % (uint)(i + 1));
            (substitution[i], substitution[j]) = (substitution[j], substitution[i]);
        }
        for (int i = 0; i < 256; i++) inverse[substitution[i]] = (byte)i;
        columns = Enumerable.Range(0, 8).OrderBy(i => key[i]).ThenBy(i => i).ToArray();
    }

    public byte[] Encrypt(byte[] input)
    {
        var transformed = input.Select(b => substitution[(b * 5 + 8) % 256]).ToArray();
        var result = new byte[input.Length];
        int p = 0;
        foreach (int c in columns)
            for (int i = c; i < transformed.Length; i += 8) result[p++] = transformed[i];
        return result;
    }

    public byte[] Decrypt(byte[] input)
    {
        var result = new byte[input.Length];
        int p = 0;
        foreach (int c in columns)
            for (int i = c; i < result.Length; i += 8) result[i] = input[p++];
        for (int i = 0; i < result.Length; i++) result[i] = (byte)(((inverse[result[i]] - 8 + 256) * 205) % 256);
        return result;
    }

    public SealedMessage Seal<T>(T value)
    {
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var at = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var payload = Convert.ToBase64String(Encrypt(JsonSerializer.SerializeToUtf8Bytes(value, JsonDefaults.Options)));
        return new(1, nonce, at, payload, Convert.ToBase64String(Sign(1, nonce, at, payload)));
    }

    public T Open<T>(SealedMessage message, Action<string> acceptNonce)
    {
        try
        {
            if (message.Version != 1 || message.Payload.Length > 1_400_000 || message.Nonce.Length != 32
                || Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - message.IssuedAt) > 120
                || !CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(message.Tag),
                    Sign(message.Version, message.Nonce, message.IssuedAt, message.Payload)))
                throw new CryptographicException();
            var bytes = Decrypt(Convert.FromBase64String(message.Payload));
            var value = JsonSerializer.Deserialize<T>(bytes, JsonDefaults.Options) ?? throw new CryptographicException();
            acceptNonce(message.Nonce);
            return value;
        }
        catch (Exception e) when (e is FormatException or JsonException or CryptographicException or NullReferenceException)
        {
            throw new BusinessException(400, "MESSAGE_REJECTED", "Message expiré, altéré ou invalide.");
        }
    }

    private byte[] Sign(int version, string nonce, long at, string payload)
        => HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"{version}\n{nonce}\n{at}\n{payload}"));
}
