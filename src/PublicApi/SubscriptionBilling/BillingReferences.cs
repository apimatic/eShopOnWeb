using System;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionBilling;

/// <summary>
/// Deterministic references sent to Maxio. They derive only from what the caller already is (their
/// user name) and asks for (the plan handle), so a repeat request maps to the same reference and the
/// same claim. The user name is hashed so no e-mail address travels in query strings or logs.
/// </summary>
public static class BillingReferences
{
    private const string CUSTOMER_PREFIX = "eshop-";

    public static string Customer(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
            throw new ArgumentException("A user name is required.", nameof(userName));

        var normalized = userName.Trim().ToUpperInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return CUSTOMER_PREFIX + Convert.ToHexString(hash, 0, 16).ToLowerInvariant();
    }

    public static string Subscription(string customerReference, string productHandle) =>
        $"{customerReference}-{productHandle}";

    public static string CustomerClaimKey(string customerReference) => $"customer:{customerReference}";

    public static string SubscriptionClaimKey(string subscriptionReference) => $"subscription:{subscriptionReference}";
}
