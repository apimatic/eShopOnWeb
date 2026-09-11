namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionResponse : BaseResponse
{
    public int SubscriptionId { get; set; }
    public string ProductHandle { get; set; } = "";
    public string State { get; set; } = "";
    public System.DateTimeOffset? NextBillingDate { get; set; }
    public decimal? Amount { get; set; }
}
