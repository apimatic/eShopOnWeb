namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public decimal PriceInDollars { get; set; }
    public string? Currency { get; set; }
    public string? NextBillingAt { get; set; }
    public string? CurrentPeriodEndsAt { get; set; }
    public string? ActivatedAt { get; set; }
    public string? CanceledAt { get; set; }
    public bool? CancelAtEndOfPeriod { get; set; }
    public string? CreatedAt { get; set; }
    public string? ProductName { get; set; }
    public string? ProductHandle { get; set; }
    public int? ProductId { get; set; }
}
