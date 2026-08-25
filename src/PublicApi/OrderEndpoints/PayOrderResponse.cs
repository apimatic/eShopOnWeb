namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? AuthorizationId { get; set; }
}
