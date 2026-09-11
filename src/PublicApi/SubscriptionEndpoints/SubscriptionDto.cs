namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public decimal Balance { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal ProductPrice { get; set; }
    public string ProductPriceDisplay { get; set; } = string.Empty;
    public string? CurrentPeriodEndsAt { get; set; }
    public string? NextAssessmentAt { get; set; }
    public string? ActivatedAt { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
    public string? CanceledAt { get; set; }
    public bool? CancelAtEndOfPeriod { get; set; }
    public string? ProductName { get; set; }
    public string? ProductHandle { get; set; }
    public string? CustomerEmail { get; set; }
    public string? Reference { get; set; }
    public string Currency { get; set; } = string.Empty;
}
