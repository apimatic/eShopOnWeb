using System;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Deterministic references sent to the billing provider. The same shopper (and plan) always yields the same
/// reference, which is what lets a repeated request find the earlier result instead of creating a second one.
/// The references carry a hash rather than the user name, so no personal data travels in a query string or log.
/// </summary>
public static class BillingReferences
{
    private const string CustomerPrefix = "eshop-cus-";
    private const string SubscriptionPrefix = "eshop-sub-";

    public static string ForCustomer(string userName) =>
        CustomerPrefix + Hash(Normalize(userName));

    public static string ForSubscription(string userName, string planHandle) =>
        SubscriptionPrefix + Hash(Normalize(userName) + "|" + planHandle.Trim().ToLowerInvariant());

    private static string Normalize(string userName) => userName.Trim().ToUpperInvariant();

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
