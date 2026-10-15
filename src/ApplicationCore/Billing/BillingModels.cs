using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>A recurring plan offered by the billing provider.</summary>
public record BillingPlan(
    int? Id,
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int? Interval,
    string? IntervalUnit);

/// <summary>The plans offered in the configured product family.</summary>
/// <param name="IsTruncated">True when paging stopped at its cap, so more plans may exist than are listed.</param>
public record PlanCatalog(string ProductFamilyHandle, IReadOnlyList<BillingPlan> Plans, bool IsTruncated);

/// <summary>A subscription as held by the billing provider (the system of record).</summary>
public record BillingSubscription(
    int Id,
    string? Reference,
    int? CustomerId,
    string State,
    string? PlanHandle,
    string? PlanName,
    string? ProductFamilyHandle,
    long? PriceInCents,
    long? CurrentBillingAmountInCents,
    string? Currency,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? CreatedAt);

/// <summary>The customer to find or create at the billing provider. <paramref name="Reference"/> is unique per provider site.</summary>
public record BillingCustomer(string Reference, string Email, string FirstName, string LastName);

/// <summary>
/// Result of a subscription create. <see cref="Subscription"/> is null when the outcome is unknown: the request
/// may have reached the provider, but neither its response nor a re-read by reference confirmed it.
/// </summary>
public record SubscriptionCreationResult(BillingSubscription? Subscription)
{
    public bool IsOutcomeUnknown => Subscription is null;

    public static SubscriptionCreationResult Created(BillingSubscription subscription) => new(subscription);
    public static SubscriptionCreationResult Unknown() => new((BillingSubscription?)null);
}
