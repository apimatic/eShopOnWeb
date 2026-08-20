using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// Maxio subscription states that mean the shopper already has this plan.
/// Live and problem states are treated as existing enrollments for idempotent subscribe.
/// </summary>
public static class SubscriptionStates
{
    public static readonly HashSet<string> Open = new(StringComparer.OrdinalIgnoreCase)
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

    public static bool IsOpen(string? state) =>
        !string.IsNullOrWhiteSpace(state) && Open.Contains(state);
}
