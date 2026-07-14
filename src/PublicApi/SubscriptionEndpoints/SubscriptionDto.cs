using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int Id { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public bool CancelAtEndOfPeriod { get; set; }
    public DateTimeOffset? DelayedCancelAt { get; set; }
    public long? BalanceInCents { get; set; }

    public static SubscriptionDto FromDomain(Subscription subscription) => new()
    {
        Id = subscription.Id,
        ProductHandle = subscription.ProductHandle,
        ProductName = subscription.ProductName,
        PriceInCents = subscription.PriceInCents,
        Status = subscription.Status.ToString(),
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextAssessmentAt = subscription.NextAssessmentAt,
        CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod,
        DelayedCancelAt = subscription.DelayedCancelAt,
        BalanceInCents = subscription.BalanceInCents
    };
}
