using System;
using Microsoft.eShopWeb.ApplicationCore.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionSummaryDto
{
    public int SubscriptionId { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public decimal? Price { get; set; }
    public string? State { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }

    public static SubscriptionSummaryDto FromServiceDto(CustomerSubscriptionDto subscription) => new()
    {
        SubscriptionId = subscription.SubscriptionId,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        Price = subscription.PriceInCents.HasValue ? subscription.PriceInCents.Value / 100m : null,
        State = subscription.State,
        NextBillingDate = subscription.NextAssessmentAt
    };
}
