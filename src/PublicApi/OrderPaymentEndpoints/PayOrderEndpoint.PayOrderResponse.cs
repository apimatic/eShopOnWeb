namespace Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;

public class PayOrderResponse
{
    public int OrderId { get; set; }

    /// <summary>The order's payment status after paying — normally "Paid".</summary>
    public string PaymentStatus { get; set; } = string.Empty;

    public string? PayPalOrderId { get; set; }
    public string? PayPalCaptureId { get; set; }
}
