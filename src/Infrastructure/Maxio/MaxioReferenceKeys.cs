using System;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Deterministic derivation of the Maxio customer/subscription <c>reference</c> values for an
/// eShopOnWeb shopper. Maxio enforces uniqueness on the customer <c>reference</c> (server-side),
/// so the key must be stable for the shopper's lifetime and must never be a Maxio numeric id
/// (ids are reassigned on re-seed). Deriving it from the username lets us re-find the customer
/// even if the local enrollment store is wiped.
/// </summary>
public static class MaxioReferenceKeys
{
    private const string CustomerPrefix = "eshop-user-";
    private const string SubscriptionPrefix = "eshop-sub-";
    private const int HashLength = 16;

    public static string CustomerReference(string userName)
    {
        ArgumentNullException.ThrowIfNullOrEmpty(userName);
        return CustomerPrefix + ShaHex(userName);
    }

    public static string SubscriptionReference(string userName, string planHandle)
    {
        ArgumentNullException.ThrowIfNullOrEmpty(userName);
        ArgumentNullException.ThrowIfNullOrEmpty(planHandle);
        return SubscriptionPrefix + planHandle + "-" + ShaHex(userName + "\n" + planHandle);
    }

    /// <summary>
    /// Recovers the plan handle embedded in a reference produced by <see cref="SubscriptionReference"/>,
    /// or null when the reference was not created by this application.
    /// </summary>
    public static string? TryGetPlanHandle(string? subscriptionReference)
    {
        if (string.IsNullOrEmpty(subscriptionReference) ||
            !subscriptionReference.StartsWith(SubscriptionPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var body = subscriptionReference.Substring(SubscriptionPrefix.Length);
        var separator = body.LastIndexOf('-');
        if (separator <= 0 || separator + 1 + HashLength != body.Length)
        {
            return null;
        }

        var hash = body.Substring(separator + 1);
        if (!IsHex(hash))
        {
            return null;
        }

        return body.Substring(0, separator);
    }

    /// <summary>
    /// Names used when a Maxio customer must first be created for a shopper. eShopOnWeb's identity
    /// stores only an email-format username, so when the client does not supply a profile the local
    /// part and the first domain label are used. Only applied on first enrollment.
    /// </summary>
    public static (string FirstName, string LastName) DeriveNames(string userName)
    {
        var at = userName.IndexOf('@');
        if (at > 0)
        {
            var domain = userName.Substring(at + 1);
            var domainLabel = domain;
            var dot = domain.IndexOf('.');
            if (dot > 0)
            {
                domainLabel = domain.Substring(0, dot);
            }

            return (userName.Substring(0, at), domainLabel);
        }

        return (userName, "Customer");
    }

    private static string ShaHex(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim().ToLowerInvariant()));
        return Convert.ToHexString(bytes, 0, HashLength / 2).ToLowerInvariant();
    }

    private static bool IsHex(string value)
    {
        foreach (var c in value)
        {
            if (!Uri.IsHexDigit(c))
            {
                return false;
            }
        }

        return true;
    }
}
