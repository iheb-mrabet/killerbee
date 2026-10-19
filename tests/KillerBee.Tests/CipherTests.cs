using KillerBee.Core;
using Xunit;

namespace KillerBee.Tests;

public class CipherTests
{
    private const string Key = "unit-test-only-generated-example-key-2026";
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(7)] [InlineData(8)] [InlineData(9)] [InlineData(256)] [InlineData(4097)]
    public void RoundTripEveryByteAndColumnBoundary(int length)
    {
        var cipher = new TeachingCipher(Key, "test");
        byte[] bytes = Enumerable.Range(0, length).Select(i => (byte)(i % 256)).ToArray();
        Assert.Equal(bytes, cipher.Decrypt(cipher.Encrypt(bytes)));
    }
    [Fact]
    public void EnvelopePreservesUnicodeAndRejectsTamperingAndWrongPurpose()
    {
        var cipher = new TeachingCipher(Key, "hop-a");
        var message = cipher.Seal(new { text = "Échantillon — مراد — 🐝", quantityCg = 12537 });
        var result = cipher.Open<System.Text.Json.JsonElement>(message, _ => { });
        Assert.Equal(12537, result.GetProperty("quantityCg").GetInt32());
        Assert.Equal("Échantillon — مراد — 🐝", result.GetProperty("text").GetString());
        Assert.Throws<BusinessException>(() => cipher.Open<object>(message with { Payload = message.Payload[..^2] + "AA" }, _ => { }));
        Assert.Throws<BusinessException>(() => new TeachingCipher(Key, "hop-b").Open<object>(message, _ => { }));
        Assert.Throws<BusinessException>(() => cipher.Open<object>(message with { IssuedAt = 0 }, _ => { }));
    }
}
