using System;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Deterministic Maxio references derived from the eShopOnWeb user name. They are the idempotency keys of the two
/// writes (Maxio keeps one customer per reference; the subscription is found again by its reference) and the claim
/// keys. Hashed, so no e-mail address ends up in a reference, a log line or a URL.
/// </summary>
public static class BillingReferences
{
    public static string Customer(string userName) => "eshop-cus-" + Hash(Normalize(userName));

    /// <summary>One subscription per user per product family.</summary>
    public static string Subscription(string userName, string productFamilyHandle) =>
        "eshop-sub-" + Hash(Normalize(userName) + "\n" + productFamilyHandle.Trim());

    private static string Normalize(string userName) => userName.Trim().ToUpperInvariant();

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..32].ToLowerInvariant();
}
