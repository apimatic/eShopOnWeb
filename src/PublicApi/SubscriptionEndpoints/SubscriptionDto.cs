using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public long MaxioSubscriptionId { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public int PriceInCents { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTime? NextBillingDateUtc { get; set; }
    public DateTime? ActivatedAtUtc { get; set; }
    public DateTime? CanceledAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public static SubscriptionDto From(UserSubscription subscription)
    {
        return new SubscriptionDto
        {
            MaxioSubscriptionId = subscription.MaxioSubscriptionId,
            PlanHandle = subscription.PlanHandle,
            PlanName = subscription.PlanName,
            State = subscription.State,
            PriceInCents = subscription.PriceInCents,
            Price = subscription.PriceInCents / 100m,
            Currency = subscription.Currency,
            NextBillingDateUtc = subscription.NextBillingAtUtc,
            ActivatedAtUtc = subscription.ActivatedAtUtc,
            CanceledAtUtc = subscription.CanceledAtUtc,
            CreatedAtUtc = subscription.CreatedAtUtc
        };
    }
}
