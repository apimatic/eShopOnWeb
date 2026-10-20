using System;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Deterministic references sent to Maxio and used as claim keys. The same buyer (and plan) always yields
/// the same reference — across requests, instances and restarts — so a repeat is recognised, not re-created.
/// They are hashes so that no personal data (the user name is an email) leaves in a reference or a log line.
/// </summary>
public static class BillingReferences
{
    public static string ForCustomer(string userName) =>
        "eshop-cus-" + Hash(Normalize(userName));

    public static string ForSubscription(string userName, string planHandle) =>
        "eshop-sub-" + Hash(Normalize(userName) + "\n" + planHandle);

    private static string Normalize(string userName) => userName.Trim().ToUpperInvariant();

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..40].ToLowerInvariant();
}
