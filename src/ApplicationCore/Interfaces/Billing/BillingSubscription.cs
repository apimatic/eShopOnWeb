using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Billing;

/// <summary>
/// The state of an enrolled subscription, as reported by the billing provider.
/// </summary>
public record BillingSubscription(
    string BillingSubscriptionId,
    string PlanHandle,
    string PlanName,
    long PriceInCents,
    string State,
    DateTimeOffset? CurrentPeriodEndsAtUtc,
    DateTimeOffset? NextBillingAtUtc);
