namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public record RefundOrderRequest
{
    public int OrderId { get; init; }
    public string BuyerId { get; init; } = "";
    public decimal? Amount { get; init; }
    public string IdempotencyKey { get; init; } = "";
}

public record RefundOrderResponse
{
    public int RefundId { get; init; }
    public string PayPalRefundId { get; init; } = "";
    public decimal Amount { get; init; }
    public string Status { get; init; } = "";
    public string Currency { get; init; } = "";
}
