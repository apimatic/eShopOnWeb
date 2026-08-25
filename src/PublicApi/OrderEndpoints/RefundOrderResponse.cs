namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderResponse
{
    public string RefundId { get; set; } = "";
    public string Status { get; set; } = "";
    public string? Amount { get; set; }
    public string? Currency { get; set; }
}
