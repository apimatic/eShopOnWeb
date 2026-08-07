namespace Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;

public class RefundOrderResponse
{
    public int OrderId { get; set; }

    /// <summary>The order's payment status after refunding — normally "Refunded".</summary>
    public string PaymentStatus { get; set; } = string.Empty;

    public string? PayPalRefundId { get; set; }
}
