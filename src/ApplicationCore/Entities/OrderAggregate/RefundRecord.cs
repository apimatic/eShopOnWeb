using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class RefundRecord : BaseEntity
{
#pragma warning disable CS8618
    private RefundRecord() { }
#pragma warning restore CS8618

    public RefundRecord(string refundId, string idempotencyKey, decimal amount, string status)
    {
        RefundId = refundId;
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        Status = status;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderPaymentId { get; private set; }
    public string RefundId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public decimal Amount { get; private set; }
    public string Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void UpdateStatus(string status) => Status = status;
}
