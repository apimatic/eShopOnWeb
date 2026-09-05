using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class UserSubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public decimal CurrentPriceInDollars { get; set; }
    public DateTime? NextAssessmentAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public SubscriptionPlanDto? Plan { get; set; }
}
