using System;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;

/// <summary>
/// Builds deterministic idempotency keys sent to PayPal as <c>PayPal-Request-Id</c>. The same
/// logical operation with the same inputs always yields the same key, so a double-click dedupes
/// at PayPal, while a genuinely different input (e.g. a different card after a decline) yields a
/// new key and is allowed to proceed.
/// </summary>
public static class IdempotencyKey
{
    /// <summary>
    /// Derives a stable key from a prefix and one or more discriminators. Sensitive inputs
    /// (such as a card number) are hashed and never appear in the returned key.
    /// </summary>
    public static string Derive(string prefix, params string[] discriminators)
    {
        var material = string.Join("|", discriminators);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        // PayPal-Request-Id allows well over 30 chars; keep it compact but collision-safe.
        return $"{prefix}-{hex[..24]}";
    }
}
