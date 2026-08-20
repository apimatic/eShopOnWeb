namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class RefundOrderRequest
{
    public string IdempotencyKey { get; set; } = string.Empty;
    public decimal? Amount { get; set; }
}

public class RefundCreatedResponse
{
    public string RefundId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal RemainingRefundable { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
}
