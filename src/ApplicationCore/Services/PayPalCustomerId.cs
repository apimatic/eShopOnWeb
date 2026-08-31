using System;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Derives the PayPal vault customer id for a shopper. PayPal requires
/// ^[0-9a-zA-Z_-]+$ with at most 22 characters, so the buyer id (an email) cannot be
/// used directly; a deterministic hash keeps the mapping stable without sending PII.
/// </summary>
public static class PayPalCustomerId
{
    public static string FromBuyerId(string buyerId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(buyerId.Trim().ToLowerInvariant()));
        return Convert.ToHexString(hash)[..22].ToLowerInvariant();
    }
}
