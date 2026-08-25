namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateRefundRequest : BaseRequest
{
    public decimal? Amount { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}
