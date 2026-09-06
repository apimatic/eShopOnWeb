using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// Classifies the billing provider's subscription states.
/// </summary>
/// <remarks>
/// Mirrors the Maxio Advanced Billing subscription state machine documented at
/// https://maxio.zendesk.com/hc/en-us/articles/24252119027853-Subscription-States.
/// A "live" state is one where the shopper is still enrolled, so subscribing again to the same
/// plan must return the existing enrolment rather than create a second one.
/// </remarks>
public static class SubscriptionStates
{
    private static readonly HashSet<string> LiveStates = new(StringComparer.OrdinalIgnoreCase)
    {
        // Live states.
        "active",
        "trialing",
        "assessing",
        "pending",
        "paused",
        "awaiting_signup",
        // Problem states: the shopper is still enrolled, the account is just not paid up.
        "past_due",
        "soft_failure",
        "unpaid",
        "suspended",
        "on_hold",
    };

    /// <summary>True when <paramref name="state"/> means the shopper is still enrolled.</summary>
    public static bool IsLive(string? state) =>
        !string.IsNullOrWhiteSpace(state) && LiveStates.Contains(state!);
}
