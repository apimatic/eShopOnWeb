using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>A subscribable plan offered by the billing system.</summary>
public record SubscriptionPlan(
    int? Id,
    string Handle,
    string Name,
    string? Description,
    long PriceInCents,
    int? Interval,
    string? IntervalUnit);

/// <summary>The plans offered for the configured product family.</summary>
/// <param name="Plans">The plans read.</param>
/// <param name="Truncated">True when the listing stopped at the page cap and more plans may exist.</param>
public record SubscriptionPlanCatalog(IReadOnlyList<SubscriptionPlan> Plans, bool Truncated);

/// <summary>A customer record in the billing system.</summary>
public record BillingCustomerRecord(int Id, string? Reference);

public record NewBillingCustomer(string Reference, string Email, string FirstName, string LastName);

public record NewBillingSubscription(string PlanHandle, int BillingCustomerId, string Reference);

/// <summary>A subscription as held by the billing system (the system of record).</summary>
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
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? CreatedAt);

public record SubscribeCommand(string BuyerId, string Email, string PlanHandle, string? FirstName, string? LastName);

public enum SubscribeOutcome
{
    /// <summary>A new subscription was created by this request.</summary>
    Created,

    /// <summary>The buyer already holds this plan; the existing subscription is returned (idempotent replay).</summary>
    Existing,

    /// <summary>The create call's outcome could not be confirmed yet; it is settled by reference on a later call.</summary>
    Pending
}

public record SubscribeResult(SubscribeOutcome Outcome, string SubscriptionReference, BillingSubscription? Subscription);

public record PendingEnrollment(string PlanHandle, string SubscriptionReference);

public record MySubscriptions(IReadOnlyList<BillingSubscription> Subscriptions, IReadOnlyList<PendingEnrollment> Pending);
