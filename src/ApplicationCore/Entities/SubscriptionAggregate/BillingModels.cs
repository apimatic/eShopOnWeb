using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>A recurring plan offered by the billing system.</summary>
public record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    long? PriceInCents,
    int? Interval,
    string? IntervalUnit);

/// <summary>
/// The plans on offer. <see cref="IsComplete"/> is false when the listing was cut short at the
/// page cap, so a plan missing from <see cref="Plans"/> may still exist.
/// </summary>
public record PlanCatalog(IReadOnlyList<SubscriptionPlan> Plans, bool IsComplete, string? DefaultPlanHandle)
{
    public SubscriptionPlan? Find(string handle) =>
        Plans.FirstOrDefault(p => string.Equals(p.Handle, handle, StringComparison.Ordinal));
}

/// <summary>A customer to ensure at the billing system.</summary>
public record BillingCustomer(string Reference, string Email, string FirstName, string LastName);

/// <summary>A subscription as held by the billing system (the system of record).</summary>
public record BillingSubscription(
    int Id,
    string? Reference,
    string? PlanHandle,
    string? PlanName,
    string State,
    long? PriceInCents,
    int? Interval,
    string? IntervalUnit,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? CreatedAt);

/// <summary>The eShopOnWeb user subscribing.</summary>
public record Subscriber(string UserName, string Email);

public enum SubscribeOutcome
{
    /// <summary>A new subscription was created.</summary>
    Created,

    /// <summary>The shopper already holds this subscription; nothing new was created.</summary>
    AlreadySubscribed,

    /// <summary>The create call's outcome is not known yet; it is settled on a later call.</summary>
    Pending
}

public record SubscribeResult(SubscribeOutcome Outcome, BillingSubscription? Subscription, string PlanHandle);

/// <summary>The shopper's subscriptions plus any request whose outcome is still being settled.</summary>
public record CustomerSubscriptions(IReadOnlyList<BillingSubscription> Subscriptions, string? PendingPlanHandle);
