using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

public sealed class ShopperAccount
{
    public required string UserId { get; init; }
    public required string Email { get; init; }
}

public sealed class SubscriptionPlan
{
    public string? Handle { get; init; }
    public string? Name { get; init; }
    public string? Description { get; init; }
    public long? PriceInCents { get; init; }
    public int? Interval { get; init; }
    public string? IntervalUnit { get; init; }
    public bool? RequireCreditCard { get; init; }
}

public sealed class SubscriptionPlanPage
{
    public required IReadOnlyList<SubscriptionPlan> Plans { get; init; }
    public bool Truncated { get; init; }
}

public sealed class ShopperSubscription
{
    public int? Id { get; init; }
    public string? Reference { get; init; }
    public string? ProductHandle { get; init; }
    public string? ProductName { get; init; }
    public long? PriceInCents { get; init; }
    public string? Currency { get; init; }
    public string? State { get; init; }
    public DateTimeOffset? NextBillingAt { get; init; }
}

public sealed class SubscribeResult
{
    public required ShopperSubscription Subscription { get; init; }
    public bool Created { get; init; }
}
