using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Seals a <see cref="PlanChangePreviewPayload"/> into an HMAC-signed, base64url token using a random key
/// generated once per process lifetime. The key never leaves memory and is never persisted or logged — the
/// token only needs to survive the few minutes between a customer previewing and committing a plan change
/// within the same running app, so a process-lifetime key (registered as a singleton) is sufficient and
/// avoids depending on any secret store from ApplicationCore.
/// </summary>
public class PlanChangePreviewTokenService : IPlanChangePreviewTokenService
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(10);

    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

    public string Protect(PlanChangePreviewPayload payload)
    {
        var json = JsonSerializer.Serialize(payload);
        var payloadBytes = Encoding.UTF8.GetBytes(json);

        using var hmac = new HMACSHA256(_key);
        var signature = hmac.ComputeHash(payloadBytes);

        return $"{Base64UrlEncode(payloadBytes)}.{Base64UrlEncode(signature)}";
    }

    public bool TryUnprotect(string token, out PlanChangePreviewPayload? payload)
    {
        payload = null;

        var parts = token.Split('.', 2);
        if (parts.Length != 2)
        {
            return false;
        }

        byte[] payloadBytes;
        byte[] signature;
        try
        {
            payloadBytes = Base64UrlDecode(parts[0]);
            signature = Base64UrlDecode(parts[1]);
        }
        catch (FormatException)
        {
            return false;
        }

        using var hmac = new HMACSHA256(_key);
        var expectedSignature = hmac.ComputeHash(payloadBytes);
        if (!CryptographicOperations.FixedTimeEquals(signature, expectedSignature))
        {
            return false;
        }

        PlanChangePreviewPayload? deserialized;
        try
        {
            deserialized = JsonSerializer.Deserialize<PlanChangePreviewPayload>(payloadBytes);
        }
        catch (JsonException)
        {
            return false;
        }

        if (deserialized is null || deserialized.ExpiresAtUtc < DateTimeOffset.UtcNow)
        {
            return false;
        }

        payload = deserialized;
        return true;
    }

    public static DateTimeOffset ComputeExpiry() => DateTimeOffset.UtcNow.Add(TokenLifetime);

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        return Convert.FromBase64String(padded);
    }
}
