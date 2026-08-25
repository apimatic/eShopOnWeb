namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderRequest : BaseMessage
{
    public decimal? Amount { get; set; }
    public string IdempotencyKey { get; set; } = "";
}
