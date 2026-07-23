using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Domain constants for the subscription feature.
/// </summary>
public static class SubscriptionConstants
{
    /// <summary>
    /// How long a proration preview may be confirmed for. A commit presented with an older preview is
    /// rejected and the customer is asked to refresh it (plan.md UC3).
    /// </summary>
    public static readonly TimeSpan PreviewValidity = TimeSpan.FromMinutes(5);

    /// <summary>Units of metered usage recorded for each order placed (plan.md §8, UC2 trigger).</summary>
    public const decimal UsageUnitsPerOrder = 1m;
}
