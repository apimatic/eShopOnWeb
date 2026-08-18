using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public sealed record ShopperIdentity(string UserId, string Email, string FirstName, string LastName);

public sealed record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int? Interval,
    string? IntervalUnit);

public sealed record ShopperSubscription(
    int Id,
    string? Reference,
    string PlanHandle,
    string PlanName,
    long PriceInCents,
    string State,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? CurrentPeriodEndsAt,
    string? Currency);

public sealed record SubscribeResult(ShopperSubscription Subscription, bool Created);

public static class ShopperName
{
    public static (string FirstName, string LastName) FromUser(string? email, string? userName)
    {
        var source = !string.IsNullOrWhiteSpace(email) ? email : userName ?? "shopper";
        var at = source.IndexOf('@');
        var local = at > 0 ? source[..at] : source;
        var parts = local.Split(new[] { '.', '_', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);
        var first = parts.Length > 0 ? Capitalize(parts[0]) : "Shopper";
        var last = parts.Length > 1 ? Capitalize(parts[^1]) : "Customer";
        return (first, last);
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        if (value.Length == 1)
        {
            return value.ToUpperInvariant();
        }

        return char.ToUpperInvariant(value[0]) + value[1..];
    }
}
