using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public static class SubscriptionStates
{
    private static readonly HashSet<string> EndOfLife = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired",
        "failed_to_create",
        "trial_ended",
        "suspended"
    };

    public static bool IsCurrent(string? state) =>
        !string.IsNullOrWhiteSpace(state) && !EndOfLife.Contains(state);
}
