using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal static class BillingMoney
{
    public static decimal FromCents(long cents) => cents / 100m;
}

internal static class SubscriptionStates
{
    private static readonly HashSet<string> Live = new(StringComparer.OrdinalIgnoreCase)
    {
        "active",
        "trialing",
        "past_due",
        "pending",
        "assessing",
        "unpaid",
        "soft_failure",
        "paused",
        "awaiting_signup"
    };

    public static bool IsLive(string? state) =>
        !string.IsNullOrWhiteSpace(state) && Live.Contains(state);
}
