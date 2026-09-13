using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int Id { get; set; }
    public string? State { get; set; }
    public decimal Price { get; set; }
    public string? Currency { get; set; }
    public string? CurrentPeriodEndsAt { get; set; }
    public string? NextAssessmentAt { get; set; }
    public string? ActivatedAt { get; set; }
    public string? CreatedAt { get; set; }
    public string? CanceledAt { get; set; }
    public string? ProductName { get; set; }
    public string? ProductHandle { get; set; }
}
