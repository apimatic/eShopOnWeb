namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class PayOrderResponse : BaseResponse
{
    public int OrderId { get; set; }

    /// <summary>Payment state after the operation: Paid.</summary>
    public string PaymentStatus { get; set; } = string.Empty;

    public string? PayPalOrderId { get; set; }

    public string? CaptureId { get; set; }

    public OrderSummaryDto Order { get; set; } = new();
}
