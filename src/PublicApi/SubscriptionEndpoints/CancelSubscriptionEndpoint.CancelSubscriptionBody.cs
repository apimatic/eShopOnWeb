namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CancelSubscriptionBody
{
    public bool EndOfPeriod { get; set; }
    public string? Reason { get; set; }
}
