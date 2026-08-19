using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

public sealed record SubscriptionDetails(
    int Id,
    string? Reference,
    string State,
    int ProductPriceInCents,
    string? ProductHandle,
    string? ProductName,
    DateTimeOffset? CurrentPeriodEndsAt,
    DateTimeOffset? NextAssessmentAt)
{
    public decimal Price => ProductPriceInCents / 100m;

    public DateTimeOffset? NextBillingDate => CurrentPeriodEndsAt ?? NextAssessmentAt;
}
