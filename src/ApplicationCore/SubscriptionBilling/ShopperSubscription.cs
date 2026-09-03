using System;

namespace Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

public sealed record ShopperSubscription(
    int? Id,
    string? Reference,
    string? State,
    string? ProductHandle,
    string? ProductName,
    long? ProductPriceInCents,
    long? CurrentBillingAmountInCents,
    DateTimeOffset? NextAssessmentAt,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? CurrentPeriodStartedAt)
{
    /// <summary>
    /// Preferred next-billing instant: assessment date, else current period end.
    /// Not an SDK member — derived for the shopper-facing confirmation.
    /// </summary>
    public DateTimeOffset? NextBillingDate => NextAssessmentAt ?? CurrentPeriodEndsAt;
}
