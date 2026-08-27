using System;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Payments;

/// <summary>
/// Maps a shopper's buyer id to the customer id used with the payment provider's vault.
/// The provider only accepts a restricted character set, so the buyer id is hashed into a
/// deterministic, safe, non-PII identifier.
/// </summary>
public static class VaultCustomerId
{
    public static string For(string buyerId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(buyerId));
        return "eshop-" + Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}
