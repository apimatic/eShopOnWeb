using System;

namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// A Maxio subscription belonging to an eShopOnWeb customer.
/// </summary>
public record CustomerSubscription(
    long SubscriptionId,
    string State,
    string PlanHandle,
    string PlanName,
    long PriceInCents,
    DateTimeOffset? NextBillingDate,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? ActivatedAt);
