using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Deterministic derivation of the Maxio customer/subscription <c>reference</c> values for an
/// eShopOnWeb shopper. Maxio enforces uniqueness on the customer <c>reference</c> and on the
/// subscription <c>reference</c> (server-side), so keys must be stable for the shopper's lifetime
/// and must never be a Maxio numeric id (ids are reassigned on re-seed). Deriving them from the
/// username lets us re-find the customer/subscription even if the local enrollment store is wiped.
///
/// A Maxio subscription keeps its <c>reference</c> for life (canceled subscriptions included), so
/// re-subscribing to a plan the shopper previously canceled needs a fresh slot: references carry a
/// generation, <c>…-g{2,3,…}</c>, chosen as one more than the highest generation Maxio already holds
/// for that (shopper, plan). Two concurrent first-time subscribes both compute generation 1 and so
/// collide on Maxio's uniqueness constraint - exactly what makes a double-click resolve to one
/// subscription.
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

    /// <summary>
    /// The reference for generation <paramref name="generation"/> of the (user, plan) subscription.
    /// Generation 1 keeps the compact form (no suffix) so first-time references are unchanged.
    /// </summary>
    public static string SubscriptionReference(string userName, string planHandle, int generation = 1)
    {
        ArgumentNullException.ThrowIfNullOrEmpty(userName);
        ArgumentNullException.ThrowIfNullOrEmpty(planHandle);

        var reference = SubscriptionPrefix + planHandle + "-" + ShaHex(userName + "\n" + planHandle);
        return generation > 1
            ? reference + "-g" + generation.ToString(CultureInfo.InvariantCulture)
            : reference;
    }

    /// <summary>
    /// Recovers the plan handle and generation embedded in a reference produced by
    /// <see cref="SubscriptionReference"/>. Returns false when the reference was not created by
    /// this application. Un-suffixed references parse as generation 1.
    /// </summary>
    public static bool TryParseSubscriptionReference(
        string? subscriptionReference, out string? planHandle, out int generation)
    {
        planHandle = null;
        generation = 1;

        if (string.IsNullOrEmpty(subscriptionReference) ||
            !subscriptionReference.StartsWith(SubscriptionPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var body = subscriptionReference.Substring(SubscriptionPrefix.Length);

        var genMarker = body.LastIndexOf("-g", StringComparison.Ordinal);
        if (genMarker > 0 &&
            int.TryParse(
                body.AsSpan(genMarker + 2), NumberStyles.None, CultureInfo.InvariantCulture, out var parsedGen) &&
            parsedGen >= 2)
        {
            generation = parsedGen;
            body = body.Substring(0, genMarker);
        }

        var separator = body.LastIndexOf('-');
        if (separator <= 0 || separator + 1 + HashLength != body.Length)
        {
            return false;
        }

        var hash = body.Substring(separator + 1);
        if (!IsHex(hash))
        {
            return false;
        }

        planHandle = body.Substring(0, separator);
        return !string.IsNullOrEmpty(planHandle);
    }

    /// <summary>
    /// Recovers the plan handle embedded in a reference produced by <see cref="SubscriptionReference"/>,
    /// or null when the reference was not created by this application.
    /// </summary>
    public static string? TryGetPlanHandle(string? subscriptionReference) =>
        TryParseSubscriptionReference(subscriptionReference, out var planHandle, out _) ? planHandle : null;

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
