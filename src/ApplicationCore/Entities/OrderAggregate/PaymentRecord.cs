namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class PaymentRecord
{
#pragma warning disable CS8618
    private PaymentRecord() { }
#pragma warning restore CS8618

    public PaymentRecord(string payPalOrderId, string authorizationId)
    {
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
    }

    public string? PayPalOrderId { get; set; }
    public string? AuthorizationId { get; set; }
    public string? CaptureId { get; set; }
    public string? CapturedAmountValue { get; set; }
    public string? CapturedAmountCurrency { get; set; }
    public string? PayPalFeeValue { get; set; }
    public string? NetAmountValue { get; set; }
    public decimal TotalRefunded { get; set; }

    internal void UpdateAuthorizationId(string newId) => AuthorizationId = newId;

    internal void RecordCapture(string captureId, string amount, string currency, string? fee, string? net)
    {
        CaptureId = captureId;
        CapturedAmountValue = amount;
        CapturedAmountCurrency = currency;
        PayPalFeeValue = fee;
        NetAmountValue = net;
    }

    internal void AddRefund(decimal amount) => TotalRefunded += amount;
}
