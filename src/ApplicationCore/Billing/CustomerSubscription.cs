using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A subscription as recorded in Maxio (the system of record for billing state).
/// </summary>
public record CustomerSubscription(
    int SubscriptionId,
    string PlanHandle,
    string PlanName,
    int PriceInCents,
    string State,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? NextBillingAt,
    DateTimeOffset CreatedAt);
