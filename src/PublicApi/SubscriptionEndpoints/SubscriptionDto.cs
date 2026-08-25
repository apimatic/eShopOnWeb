using System;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public long Id { get; set; }

    public string State { get; set; } = string.Empty;

    public string ProductHandle { get; set; } = string.Empty;

    public string ProductName { get; set; } = string.Empty;

    public long PriceInCents { get; set; }

    public string? Currency { get; set; }

    public string? PaymentCollectionMethod { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    /// <summary>
    /// End of the current billing period; for a renewing subscription this is the next billing date.
    /// </summary>
    public DateTimeOffset? NextBillingDate { get; set; }

    public static SubscriptionDto FromMaxio(MaxioSubscription subscription)
    {
        return new SubscriptionDto
        {
            Id = subscription.Id,
            State = subscription.State ?? string.Empty,
            ProductHandle = subscription.Product?.Handle ?? string.Empty,
            ProductName = subscription.Product?.Name ?? string.Empty,
            PriceInCents = subscription.ProductPriceInCents,
            Currency = subscription.Currency,
            PaymentCollectionMethod = subscription.PaymentCollectionMethod,
            ActivatedAt = subscription.ActivatedAt,
            NextBillingDate = subscription.CurrentPeriodEndsAt
        };
    }
}
