namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderResponse
{
    public int OrderId { get; set; }
    public string? Status { get; set; }
}
