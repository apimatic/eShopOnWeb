namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public PaymentDto? Payment { get; set; }
}
