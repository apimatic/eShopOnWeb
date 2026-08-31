namespace Microsoft.eShopWeb.ApplicationCore.Models.Payments;

/// <summary>A refund as reported by the payment gateway.</summary>
public class PaymentGatewayRefund
{
    public string RefundId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
}
