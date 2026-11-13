using System;
using System.Security.Cryptography;
using System.Text;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

/// <summary>
/// Deterministic references sent to Maxio. The same input always yields the same reference, so a repeated
/// request lands on the same claim row and the same provider record. They are hashes: the user name (an
/// email address) never reaches Maxio's reference fields, URLs or logs.
/// </summary>
public static class MaxioReferences
{
    private const string CustomerPrefix = "eshop-cus-";
    private const string SubscriptionPrefix = "eshop-sub-";

    /// <summary>The Maxio customer reference for an eShopOnWeb user (case-insensitive on the user name).</summary>
    public static string ForCustomer(string userName)
    {
        Guard.Against.NullOrWhiteSpace(userName, nameof(userName));
        return CustomerPrefix + Hash(userName.Trim().ToUpperInvariant());
    }

    /// <summary>The Maxio subscription reference for one user subscribing to one plan.</summary>
    public static string ForSubscription(string customerReference, string planHandle)
    {
        Guard.Against.NullOrWhiteSpace(customerReference, nameof(customerReference));
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));
        return SubscriptionPrefix + Hash(customerReference + "\n" + planHandle);
    }

    private static string Hash(string value)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(digest, 0, 16).ToLowerInvariant();
    }
}
