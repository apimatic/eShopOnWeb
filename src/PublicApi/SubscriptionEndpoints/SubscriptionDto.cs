using System;
using Maxio.Models;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription exposed to API clients.
/// </summary>
public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public decimal Price { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public string? PaymentCollectionMethod { get; set; }

    public static SubscriptionDto FromSubscription(Subscription subscription)
    {
        return new SubscriptionDto
        {
            Id = subscription.Id,
            State = subscription.State ?? string.Empty,
            PlanHandle = subscription.Product?.Handle ?? string.Empty,
            PlanName = subscription.Product?.Name ?? string.Empty,
            PriceInCents = subscription.ProductPriceInCents,
            Price = subscription.ProductPriceInCents / 100m,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextAssessmentAt = subscription.NextAssessmentAt,
            ActivatedAt = subscription.ActivatedAt,
            CreatedAt = subscription.CreatedAt,
            PaymentCollectionMethod = subscription.PaymentCollectionMethod
        };
    }
}
