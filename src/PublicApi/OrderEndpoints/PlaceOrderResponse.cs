namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PlaceOrderResponse
{
    public int OrderId { get; set; }
    public decimal Total { get; set; }
    public string Status { get; set; } = string.Empty;
}
