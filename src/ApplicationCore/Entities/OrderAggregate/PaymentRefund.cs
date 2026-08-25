namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class PaymentRefund : BaseEntity
{
#pragma warning disable CS8618
    private PaymentRefund() { }
#pragma warning restore CS8618

    public PaymentRefund(string? refundId, string? refundStatus, string? amount, string? currency, string idempotencyKey)
    {
        RefundId = refundId;
        RefundStatus = refundStatus;
        Amount = amount;
        Currency = currency;
        IdempotencyKey = idempotencyKey;
    }

    public string? RefundId { get; private set; }
    public string? RefundStatus { get; private set; }
    public string? Amount { get; private set; }
    public string? Currency { get; private set; }
    public string IdempotencyKey { get; private set; }
}
