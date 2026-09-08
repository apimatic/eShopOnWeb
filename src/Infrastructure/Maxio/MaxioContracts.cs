using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>A billable plan exposed by the Maxio product family (a Maxio "product").</summary>
public sealed record SubscriptionPlan(
    string Handle,
    string Name,
    long PriceInCents,
    int? Interval,
    string IntervalUnit,
    bool RequiresPaymentMethod);

/// <summary>A subscription owned by one Maxio customer (a Maxio "subscription").</summary>
public sealed record SubscriptionRecord(
    int? SubscriptionId,
    string? PlanHandle,
    string? PlanName,
    long? PriceInCents,
    string? Currency,
    string State,
    DateTimeOffset? NextBillingDate);

/// <summary>Identity attributes needed to ensure a Maxio customer for the calling eShopOnWeb user.</summary>
public sealed record SubscriptionCustomerProfile(
    string CustomerReference,
    string Email,
    string FirstName,
    string LastName);

/// <summary>Request to enroll the current user in a plan.</summary>
public sealed record SubscribeToPlanRequest(
    SubscriptionCustomerProfile Customer,
    string PlanHandle);

/// <summary>Outcome of an idempotent subscribe attempt.</summary>
public sealed record SubscribeResult(SubscriptionRecord Subscription, bool IsNew);
