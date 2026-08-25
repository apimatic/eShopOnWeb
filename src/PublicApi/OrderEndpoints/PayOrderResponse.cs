namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderResponse
{
    public string AuthorizationId { get; set; } = "";
    public string Status { get; set; } = "";
    public string PayPalOrderId { get; set; } = "";
}
