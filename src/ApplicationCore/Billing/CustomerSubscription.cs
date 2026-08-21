using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A shopper's subscription as recorded by Maxio Advanced Billing.
/// </summary>
public sealed record CustomerSubscription(
    int Id,
    string? Reference,
    string State,
    string PlanHandle,
    string PlanName,
    int PriceInCents,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset? CurrentPeriodEndsAt);
