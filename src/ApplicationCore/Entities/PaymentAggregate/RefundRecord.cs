using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class RefundRecord
{
    private RefundRecord() { }

    public RefundRecord(int orderId, string idempotencyKey, string finixTransferId, long amountCents, string status)
    {
        Id = Guid.NewGuid().ToString("N");
        OrderId = orderId;
        IdempotencyKey = idempotencyKey;
        FinixTransferId = finixTransferId;
        AmountCents = amountCents;
        Status = status;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string Id { get; private set; } = string.Empty;
    public int OrderId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string FinixTransferId { get; private set; } = string.Empty;
    public long AmountCents { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    public void MarkConfirmed(string finixTransferId, string status)
    {
        FinixTransferId = finixTransferId;
        Status = status;
    }
}
