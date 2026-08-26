using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public long Id { get; set; }

    public string State { get; set; } = string.Empty;

    public string Reference { get; set; } = string.Empty;

    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    public long PriceInCents { get; set; }

    public DateTime? NextBillingAt { get; set; }

    public DateTime? ActivatedAt { get; set; }

    public DateTime? CreatedAt { get; set; }
}
