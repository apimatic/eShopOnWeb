using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

/// <summary>A recurring plan offered by the billing system.</summary>
public record SubscriptionPlan(
    int? ProductId,
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int? Interval,
    string? IntervalUnit);

/// <summary>The plans offered for subscription. <see cref="IsTruncated"/> is true when the listing hit its page cap.</summary>
public record SubscriptionPlanCatalog(IReadOnlyList<SubscriptionPlan> Plans, bool IsTruncated);

public record BillingCustomerAccount(int Id, string? Reference);

public record NewBillingCustomer(string Reference, string FirstName, string LastName, string Email);

public record NewBillingSubscription(int CustomerId, string PlanHandle, string Reference);

/// <summary>A subscription as recorded by the billing system (the system of record).</summary>
public record BillingSubscription(
    int Id,
    string? Reference,
    string State,
    string? PlanHandle,
    string? PlanName,
    long? PriceInCents,
    string? Currency,
    int? Interval,
    string? IntervalUnit,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? CreatedAt);

/// <summary>An eShopOnWeb user as seen by the subscription feature.</summary>
public record Shopper(string UserId, string UserName, string Email);

/// <summary>A subscription whose creation has been requested but not yet confirmed by the billing system.</summary>
public record PendingSubscription(string PlanHandle, string Reference, DateTimeOffset RequestedAt);

public record SubscribeResult(BillingSubscription Subscription, bool AlreadySubscribed);

public record MySubscriptions(IReadOnlyList<BillingSubscription> Subscriptions, IReadOnlyList<PendingSubscription> Pending);
