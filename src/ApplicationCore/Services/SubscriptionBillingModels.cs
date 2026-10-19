using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public sealed class ShopperProfile
{
    public required string UserId { get; init; }
    public required string Email { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
}

public sealed class SubscriptionPlan
{
    public required string Handle { get; init; }
    public string? Name { get; init; }
    public long? PriceInCents { get; init; }
    public int? Interval { get; init; }
    public string? IntervalUnit { get; init; }
}

public sealed class PlanCatalog
{
    public required IReadOnlyList<SubscriptionPlan> Plans { get; init; }
    public bool Truncated { get; init; }
}

public sealed class ShopperSubscription
{
    public string? Reference { get; init; }
    public string? PlanHandle { get; init; }
    public string? PlanName { get; init; }
    public long? PriceInCents { get; init; }
    public string? State { get; init; }
    public DateTimeOffset? NextBillingAt { get; init; }
    public DateTimeOffset? NextAssessmentAt { get; init; }
}

public sealed class SubscribeResult
{
    public required ShopperSubscription Subscription { get; init; }
    public bool Created { get; init; }
}
