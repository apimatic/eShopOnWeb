using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int Id { get; set; }
    public string? ProductHandle { get; set; }
    public string? ProductName { get; set; }
    public decimal Price { get; set; }
    public string? State { get; set; }
    public DateTime? CurrentPeriodStartedAt { get; set; }
    public DateTime? CurrentPeriodEndsAt { get; set; }
    public DateTime? NextAssessmentAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
