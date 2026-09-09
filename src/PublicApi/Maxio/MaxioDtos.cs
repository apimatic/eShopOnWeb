using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// A subscription plan (a Maxio product in the configured product family).
/// </summary>
public sealed record SubscriptionPlanDto(
    string Handle,
    string Name,
    long PriceInCents,
    string? BillingInterval,
    string? IntervalUnit,
    bool? RequestCreditCard,
    bool? RequireCreditCard);

/// <summary>
/// A user's subscription as confirmed by Maxio Advanced Billing.
/// </summary>
public sealed record SubscriptionSummaryDto(
    int SubscriptionId,
    string PlanHandle,
    string PlanName,
    long PriceInCents,
    string State,
    DateTimeOffset? NextBillingDate,
    string? Currency,
    string? Reference,
    bool AlreadySubscribed);
