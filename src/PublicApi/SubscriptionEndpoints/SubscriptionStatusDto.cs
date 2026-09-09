namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The current state of the caller's subscription, as confirmed by Maxio.
/// </summary>
public class SubscriptionStatusDto
{
    public int MaxioSubscriptionId { get; set; }
    public int MaxioCustomerId { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int PriceInCents { get; set; }
    public string PriceDisplay { get; set; } = string.Empty;
    public string Currency { get; set; } = "USD";
    public string State { get; set; } = string.Empty;
    public string? NextBillingDateUtc { get; set; }
    public string? CreatedAtUtc { get; set; }
}
