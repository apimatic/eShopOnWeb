using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>
/// Classifies the subscription states defined by the billing system's Subscription-State contract.
/// A "live" subscription is one that is currently providing (or will provide) service, and therefore
/// should block a duplicate enrollment to the same plan; the remaining states are end-of-life and a
/// user may freshly subscribe again.
/// </summary>
public static class SubscriptionState
{
    // States in which a subscription is still providing service or in the process of starting.
    // Enrolling again in the same plan while one of these exists would create a duplicate.
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "pending",
        "awaiting_signup",
        "trialing",
        "assessing",
        "active",
        "soft_failure",
        "past_due",
        "paused",
        "suspended",
    };

    public static bool IsLive(string? state) =>
        !string.IsNullOrWhiteSpace(state) && LiveStates.Contains(state);
}
