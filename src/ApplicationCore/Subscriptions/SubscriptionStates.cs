using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Classifies the lifecycle states a billing subscription can report.
/// </summary>
public static class SubscriptionStates
{
    /// <summary>
    /// States a subscription can never come back from. A shopper sitting in one of these is free to
    /// subscribe to the same plan again, so they do not satisfy an idempotent subscribe request.
    /// </summary>
    private static readonly HashSet<string> EndOfLife = new(StringComparer.OrdinalIgnoreCase)
    {
        "canceled",
        "expired",
        "failed_to_create",
        "trial_ended"
    };

    /// <summary>
    /// True while the subscription still occupies the shopper's slot on that plan. This deliberately
    /// includes problem states such as "past_due" and transient ones such as "pending": enrolling again
    /// while one of those is in flight would bill the shopper twice.
    /// </summary>
    public static bool IsLive(string? state) =>
        !string.IsNullOrWhiteSpace(state) && !EndOfLife.Contains(state);
}
