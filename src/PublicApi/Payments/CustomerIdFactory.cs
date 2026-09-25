using System;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Derives a stable, PayPal-safe customer id from a buyer's email/username. PayPal's customer id
/// must match <c>^[0-9a-zA-Z_-]+$</c> and be at most 22 characters, so the raw email (which contains
/// '@' and '.') cannot be used directly. The same buyer always maps to the same id.
/// </summary>
public static class CustomerIdFactory
{
    public static string ForBuyer(string buyerId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(buyerId));
        // Base64url uses only [A-Za-z0-9-_]; trim to 20 chars (<= PayPal's 22-char limit).
        var base64 = Convert.ToBase64String(hash)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
        return base64[..20];
    }
}
