using System;
using System.Globalization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Builds the reference strings that tie Maxio records back to eShopOnWeb.
/// </summary>
/// <remarks>
/// References are the join key between the two systems, so they must be deterministic: the same
/// user and plan always produce the same reference, which is what makes the signup flow safe to
/// repeat. The <c>eshop</c> prefix namespaces these records on sites shared with other apps.
/// </remarks>
internal static class MaxioReferences
{
    private const string Prefix = "eshop";

    /// <summary>The customer reference for an eShopOnWeb user.</summary>
    public static string ForCustomer(string userIdentifier) =>
        $"{Prefix}-{Normalize(userIdentifier)}";

    /// <summary>The subscription reference for one user's enrollment on one plan.</summary>
    public static string ForSubscription(string userIdentifier, string planHandle) =>
        $"{Prefix}-{Normalize(userIdentifier)}-{Normalize(planHandle)}";

    private static string Normalize(string value) =>
        value.Trim().ToLower(CultureInfo.InvariantCulture);
}
