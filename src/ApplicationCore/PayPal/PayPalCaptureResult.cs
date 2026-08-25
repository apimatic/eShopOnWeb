namespace Microsoft.eShopWeb.ApplicationCore.PayPal;

public class PayPalCaptureResult
{
    public PayPalCaptureResult(string captureId, string status, decimal amount, decimal? feeAmount, decimal? netAmount)
    {
        CaptureId = captureId;
        Status = status;
        Amount = amount;
        FeeAmount = feeAmount;
        NetAmount = netAmount;
    }

    public string CaptureId { get; }
    public string Status { get; }
    public decimal Amount { get; }
    public decimal? FeeAmount { get; }
    public decimal? NetAmount { get; }
}
