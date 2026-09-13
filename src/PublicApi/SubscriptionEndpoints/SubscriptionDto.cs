namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public int ProductId { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public long PriceInCents { get; set; }
    public long CurrentBillingAmountInCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public string? NextAssessmentAt { get; set; }
    public string? CurrentPeriodEndsAt { get; set; }
    public string? ActivatedAt { get; set; }
}
