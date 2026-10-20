using System;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Deterministic, PII-free references. The same shop user always maps to the same customer reference,
/// and the same user + plan always maps to the same subscription reference, across restarts.
/// </summary>
public static class BillingReferences
{
    public const string CustomerPrefix = "eshop-user-";
    public const string SubscriptionPrefix = "eshop-sub-";

    // 32 hex chars = 128 bits of SHA-256; with the prefixes the references stay well under 64 chars.
    private const int HashHexLength = 32;

    public static string ForCustomer(string userName)
    {
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new ArgumentException("A user name is required.", nameof(userName));
        }
        return CustomerPrefix + Hash(userName.Trim().ToUpperInvariant());
    }

    public static string ForSubscription(string customerReference, string planHandle)
    {
        if (string.IsNullOrWhiteSpace(customerReference))
        {
            throw new ArgumentException("A customer reference is required.", nameof(customerReference));
        }
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new ArgumentException("A plan handle is required.", nameof(planHandle));
        }
        return SubscriptionPrefix + Hash(customerReference + "|" + planHandle.Trim().ToLowerInvariant());
    }

    private static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..HashHexLength];
    }
}
