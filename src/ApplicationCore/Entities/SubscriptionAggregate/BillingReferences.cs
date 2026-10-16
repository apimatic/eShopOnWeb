using System;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Deterministic references this app hands to the billing system. They are derived from what a request is for
/// (never random), so a retried or duplicated request maps to the same billing record, and they are hashed so no
/// buyer e-mail or user name ends up in a billing reference or a lookup URL.
/// </summary>
public static class BillingReferences
{
    private const int HashChars = 32;

    public static string ForCustomer(string buyerId) =>
        "eshop-cust-" + Hash(buyerId.Trim().ToLowerInvariant());

    public static string ForSubscription(string claimKey) =>
        "eshop-sub-" + Hash(claimKey);

    private static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes)[..HashChars].ToLowerInvariant();
    }
}
