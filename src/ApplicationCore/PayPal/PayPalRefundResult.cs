namespace Microsoft.eShopWeb.ApplicationCore.PayPal;

public class PayPalRefundResult
{
    public PayPalRefundResult(string refundId, string status, decimal amount)
    {
        RefundId = refundId;
        Status = status;
        Amount = amount;
    }

    public string RefundId { get; }
    public string Status { get; }
    public decimal Amount { get; }
}
