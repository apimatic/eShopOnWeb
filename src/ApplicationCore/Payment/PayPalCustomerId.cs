using System;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.eShopWeb.ApplicationCore.Payment;

public static class PayPalCustomerId
{
    /// <summary>
    /// PayPal vault customer ids are [0-9a-zA-Z_-], max 22 characters.
    /// Identity user names (emails) are not valid as-is.
    /// </summary>
    public static string ForBuyer(string buyerId)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
            throw new ArgumentException("Buyer id is required.", nameof(buyerId));

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(buyerId));
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        return hex[..22];
    }
}
