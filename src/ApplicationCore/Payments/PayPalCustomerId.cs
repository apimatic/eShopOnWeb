using System;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Derives the PayPal vault customer id for a buyer. The vault spec constrains
/// customer ids to ^[0-9a-zA-Z_-]+$ with a maximum of 22 characters, which
/// eShop usernames (emails) do not satisfy, so a deterministic, non-reversible
/// id is derived instead.
/// </summary>
public static class PayPalCustomerId
{
    public static string ForBuyer(string buyerId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(buyerId.ToLowerInvariant()));
        return "eshop-" + Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}
