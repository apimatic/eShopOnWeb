using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>A recurring plan offered by the billing provider's configured product family.</summary>
public sealed record BillingPlan(
    int Id,
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int? Interval,
    string? IntervalUnit,
    bool RequiresPaymentMethod);

/// <param name="IsTruncated">True when the provider returned more plans than the read cap allowed; <see cref="Plans"/> is then partial.</param>
public sealed record BillingPlanCatalog(string ProductFamilyHandle, IReadOnlyList<BillingPlan> Plans, bool IsTruncated);

public sealed record BillingCustomer(int Id, string? Reference, string? Email);

public sealed record NewBillingCustomer(string Reference, string FirstName, string LastName, string Email);

public sealed record BillingSubscription(
    int Id,
    string? Reference,
    int? CustomerId,
    string State,
    bool IsLive,
    string? PlanHandle,
    string? PlanName,
    long? PriceInCents,
    string? Currency,
    int? Interval,
    string? IntervalUnit,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? CreatedAt);

/// <param name="Created">False when an existing subscription was returned instead of creating one.</param>
public sealed record SubscribeResult(BillingSubscription Subscription, bool Created);

public sealed record SubscribeOutcome(SubscribeResult? Result, bool OutcomeUnknown)
{
    public static SubscribeOutcome Confirmed(SubscribeResult result) => new(result, false);
    public static SubscribeOutcome Unknown() => new(null, true);
}

public sealed record MySubscriptions(IReadOnlyList<BillingSubscription> Subscriptions, bool HasUnconfirmedEnrollment);

public sealed record BuyerProfile(string BuyerId, string Email, string? FirstName, string? LastName);
