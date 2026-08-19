using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Billing;

/// <summary>
/// Live vs end-of-life Maxio subscription states (from the OpenAPI <c>Subscription-State</c> schema).
/// </summary>
public static class SubscriptionStates
{
    private static readonly HashSet<string> Live = new(StringComparer.OrdinalIgnoreCase)
    {
        "pending",
        "trialing",
        "assessing",
        "active",
        "soft_failure",
        "past_due",
        "paused",
        "unpaid",
        "awaiting_signup"
    };

    public static bool IsLive(string? state) =>
        !string.IsNullOrWhiteSpace(state) && Live.Contains(state);
}
