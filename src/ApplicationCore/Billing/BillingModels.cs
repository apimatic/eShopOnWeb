using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    decimal Price,
    int? Interval,
    string? IntervalUnit,
    bool RequiresPaymentMethod);

public record UserSubscription(
    long SubscriptionId,
    string PlanHandle,
    string PlanName,
    decimal Price,
    string State,
    DateTimeOffset? NextBillingDate);

/// <param name="Created">False when the user already had a live subscription to the plan (idempotent replay).</param>
public record SubscribeResult(UserSubscription Subscription, bool Created);
