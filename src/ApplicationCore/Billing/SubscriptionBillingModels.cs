using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>A recurring plan offered for subscription.</summary>
public sealed record SubscriptionPlan(
    string Handle,
    string Name,
    string? Description,
    long? PriceInCents,
    int? Interval,
    string? IntervalUnit,
    bool RequiresPaymentMethod);

/// <summary>
/// The plans offered for subscription. <see cref="IsComplete"/> is false when the provider listing was cut
/// short by the page cap, so plans may be missing from <see cref="Plans"/>.
/// </summary>
public sealed record PlanCatalog(IReadOnlyList<SubscriptionPlan> Plans, bool IsComplete);

/// <summary>A subscription as the billing provider reports it.</summary>
public sealed record SubscriptionDetails(
    long Id,
    string? Reference,
    string? PlanHandle,
    string? PlanName,
    long? PriceInCents,
    int? Interval,
    string? IntervalUnit,
    string? State,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? NextAssessmentAt,
    DateTimeOffset? CreatedAt);

/// <summary>Who is subscribing, as known to eShopOnWeb.</summary>
public sealed record BillingCustomerProfile(string UserName, string Email, string FirstName, string LastName);

public enum SubscribeOutcome
{
    /// <summary>A new subscription was created.</summary>
    Created,

    /// <summary>The user already had this subscription; nothing new was created.</summary>
    AlreadyExists,

    /// <summary>The provider may have created the subscription, but it could not be confirmed yet.</summary>
    Pending
}

public sealed record SubscribeResult(SubscribeOutcome Outcome, string Reference, SubscriptionDetails? Subscription);
