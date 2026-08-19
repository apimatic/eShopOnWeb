using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed record ShopperIdentity(string UserId, string Email, string? UserName);

public sealed record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    decimal Price,
    long PriceInCents,
    int Interval,
    string IntervalUnit);

public sealed record ShopperSubscription(
    int Id,
    string State,
    string? ProductHandle,
    string? ProductName,
    decimal Price,
    long PriceInCents,
    DateTimeOffset? NextBillingAt);

public sealed record BillingCustomer(int Id, string? Reference, string Email);

public sealed record NewBillingCustomer(string FirstName, string LastName, string Email, string Reference);

public sealed record SubscribeResult(ShopperSubscription Subscription, bool Created);

public static class SubscriptionStates
{
    private static readonly HashSet<string> Live = new(StringComparer.OrdinalIgnoreCase)
    {
        "active",
        "trialing",
        "pending",
        "assessing",
        "past_due",
        "soft_failure",
        "unpaid",
        "paused",
        "awaiting_signup"
    };

    public static bool IsLive(string? state) =>
        !string.IsNullOrWhiteSpace(state) && Live.Contains(state);
}
