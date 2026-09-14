using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionServices;

public class SubscriptionDetailsDto
{
    public int? Id { get; set; }

    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    public string? State { get; set; }

    public long? PriceInCents { get; set; }

    public decimal? Price { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    public DateTimeOffset? NextBillingDate { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }
}
