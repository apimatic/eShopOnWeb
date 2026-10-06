using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>A subscribable plan, backed by a Maxio product in the configured product family.</summary>
public sealed record SubscriptionPlanDto
{
    public string Handle { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public long PriceInCents { get; init; }
    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;
}

/// <summary>A subscription of an eShopOnWeb user, backed by a Maxio subscription.</summary>
public sealed record SubscriptionDto
{
    public int SubscriptionId { get; init; }
    public string PlanHandle { get; init; } = string.Empty;
    public string PlanName { get; init; } = string.Empty;
    public long PriceInCents { get; init; }
    public string Currency { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public DateTimeOffset? NextBillingAtUtc { get; init; }
    public DateTimeOffset? CreatedAtUtc { get; init; }

    /// <summary>True when the enrollment request found an existing subscription instead of creating one.</summary>
    public bool AlreadySubscribed { get; init; }
}