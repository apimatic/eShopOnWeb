namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderResponse
{
    public string? RefundId { get; set; }
    public decimal Amount { get; set; }
    public string? Currency { get; set; }
    public decimal TotalRefunded { get; set; }
}
