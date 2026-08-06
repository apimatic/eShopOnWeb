namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class RefundOrderResponse : BaseResponse
{
    public int OrderId { get; set; }

    /// <summary>Payment state after the operation: Refunded.</summary>
    public string PaymentStatus { get; set; } = string.Empty;

    public string? RefundId { get; set; }

    public OrderSummaryDto Order { get; set; } = new();
}
