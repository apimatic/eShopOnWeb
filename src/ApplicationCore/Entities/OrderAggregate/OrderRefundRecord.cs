namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderRefundRecord : BaseEntity
{
#pragma warning disable CS8618
    private OrderRefundRecord() { }
#pragma warning restore CS8618

    public OrderRefundRecord(
        string payPalRefundId,
        string status,
        decimal amount,
        string currency,
        string idempotencyKey,
        decimal? totalRefundedAmount)
    {
        PayPalRefundId = payPalRefundId;
        Status = status;
        Amount = amount;
        Currency = currency;
        IdempotencyKey = idempotencyKey;
        TotalRefundedAmount = totalRefundedAmount;
    }

    public string PayPalRefundId { get; private set; }
    public string Status { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public string IdempotencyKey { get; private set; }
    public decimal? TotalRefundedAmount { get; private set; }
}
