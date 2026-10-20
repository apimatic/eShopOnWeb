using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public record PlanDto(
    string Handle,
    string Name,
    string? Description,
    long? PriceInCents,
    int? Interval,
    string? IntervalUnit);

/// <summary>Plans offered; <see cref="IsPartial"/> is true when the provider list was cut short by the page cap.</summary>
public record PlanList(IReadOnlyList<PlanDto> Plans, bool IsPartial);

public record SubscriptionDto(
    int? SubscriptionId,
    string? PlanHandle,
    string? PlanName,
    long? PriceInCents,
    int? Interval,
    string? IntervalUnit,
    string? State,
    DateTimeOffset? NextBillingAt);

public enum SubscribeOutcome
{
    /// <summary>A new subscription was created.</summary>
    Created,
    /// <summary>The user already had this subscription (idempotent replay or reconciled earlier attempt).</summary>
    Existing,
    /// <summary>The provider write may or may not have landed; it is recorded as unknown and settled on the next request.</summary>
    Unknown
}

public record SubscribeResult(SubscribeOutcome Outcome, SubscriptionDto? Subscription);
