namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderRequest : BaseRequest
{
    public string? IdempotencyKey { get; set; }
    public decimal? Amount { get; set; }
}
