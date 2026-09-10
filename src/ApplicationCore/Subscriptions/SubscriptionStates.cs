using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Well-known Maxio subscription states relevant to eShopOnWeb's subscribe flow.
/// </summary>
public static class SubscriptionStates
{
    /// <summary>
    /// Terminal / dead states. A subscription in one of these does not block re-subscribing to the same
    /// plan, so a churned shopper can subscribe again.
    /// </summary>
    public static readonly IReadOnlySet<string> Inactive = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired",
        "failed_to_create",
    };
}
