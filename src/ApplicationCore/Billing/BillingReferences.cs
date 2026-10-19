using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// Deterministic references sent to the billing provider. They are derived from the eShop user name
/// (stable across restarts, unlike identity ids under the in-memory store) so the same user always
/// maps to the same billing customer and the same (user, plan) to the same subscription.
/// </summary>
public static class BillingReferences
{
    public const string CustomerPrefix = "eshop-user:";
    public const string SubscriptionPrefix = "eshop-sub:";

    public static string NormalizeUserName(string userName)
    {
        Guard.Against.NullOrWhiteSpace(userName, nameof(userName));
        return userName.Trim().ToLowerInvariant();
    }

    public static string CustomerReference(string normalizedUserName) =>
        CustomerPrefix + normalizedUserName;

    public static string SubscriptionReference(string normalizedUserName, string planHandle)
    {
        Guard.Against.NullOrWhiteSpace(planHandle, nameof(planHandle));
        return $"{SubscriptionPrefix}{normalizedUserName}:{planHandle.Trim().ToLowerInvariant()}";
    }

    public static bool IsSubscriptionReferenceFor(string? reference, string normalizedUserName) =>
        reference is not null &&
        reference.StartsWith($"{SubscriptionPrefix}{normalizedUserName}:", StringComparison.Ordinal);
}
