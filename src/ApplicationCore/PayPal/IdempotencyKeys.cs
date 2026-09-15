using System;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.eShopWeb.ApplicationCore.PayPal;

/// <summary>
/// Builds stable, provider-safe idempotency keys (PayPal-Request-Id values). Keys are deterministic per
/// logical action so a retry or double-click re-sends the same value and PayPal never charges twice.
/// </summary>
public static class IdempotencyKeys
{
    /// <summary>Lower-case hex of the first 8 bytes (16 chars) of the SHA-256 of <paramref name="input"/>.</summary>
    public static string ShortHash(string input)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
        var sb = new StringBuilder(16);
        for (var i = 0; i < 8; i++)
        {
            sb.Append(bytes[i].ToString("x2"));
        }
        return sb.ToString();
    }

    /// <summary>Key for the create+authorize of an order's payment.</summary>
    public static string Authorize(int paymentId) => $"esh-auth-{paymentId}";

    /// <summary>
    /// Key for capturing a specific authorization. Bound to the authorization id so a capture on a renewed
    /// authorization uses a distinct key, while a double-click on the same authorization reuses it.
    /// </summary>
    public static string Capture(int paymentId, string authorizationId)
        => $"esh-cap-{paymentId}-{ShortHash(authorizationId)}";

    /// <summary>Provider request-id for a refund, derived from the caller-supplied key (deduped separately in our DB).</summary>
    public static string Refund(int paymentId, string callerKey)
        => $"esh-ref-{ShortHash(paymentId + "|" + callerKey)}";

    /// <summary>A stable PayPal customer id for a shopper, valid for the vault customer.id field.</summary>
    public static string Customer(string buyerId) => $"esh_{ShortHash(buyerId)}";
}
