namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderResponse
{
    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? ExpirationTime { get; set; }
}
