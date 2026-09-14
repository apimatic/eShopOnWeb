using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Classification of billing subscription states.
/// Values and their meaning come from the Maxio Advanced Billing OpenAPI specification
/// (components/schemas/Subscription-State.yaml).
/// </summary>
public static class SubscriptionStates
{
    /// <summary>
    /// States in which a subscription already exists for billing purposes - either healthy
    /// ("Live States") or recoverable ("Problem States"), plus on_hold which is expected to resume. A shopper who already has one of these
    /// for a plan must not be enrolled a second time.
    /// </summary>
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "pending",
        "trialing",
        "assessing",
        "active",
        "soft_failure",
        "past_due",
        "paused",
        "suspended",
        "unpaid",
        "on_hold",
        "awaiting_signup"
    };

    public static bool IsLive(string? state) => state is not null && LiveStates.Contains(state);
}
