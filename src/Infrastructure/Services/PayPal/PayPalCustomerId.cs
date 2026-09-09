using System;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// Derives a stable PayPal customer id from a shopper's identity so all of that shopper's vaulted
/// cards are grouped under one PayPal customer, and re-runs map the same shopper to the same id. The
/// output is pattern-safe for PayPal (alphanumeric, dashes/underscores) and contains no PII.
/// </summary>
public static class PayPalCustomerId
{
    public static string For(string buyerId)
    {
        // PayPal's customer.id is capped at 22 characters, so keep the prefix + 16 hex chars = 22.
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(buyerId));
        var hex = Convert.ToHexString(bytes).ToLowerInvariant();
        return "eshop-" + hex[..16];
    }
}
