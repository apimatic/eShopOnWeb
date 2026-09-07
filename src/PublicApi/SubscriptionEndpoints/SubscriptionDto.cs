using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int? Id { get; set; }
    public string? State { get; set; }
    public long? ProductPriceInCents { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? NextAssessmentAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    public decimal GetPrice() => ProductPriceInCents.HasValue ? ProductPriceInCents.Value / 100m : 0m;
}
