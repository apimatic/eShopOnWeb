namespace Microsoft.eShopWeb.PublicApi.Services;

public class SubscriptionResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public SubscriptionDto? Subscription { get; set; }
}
