using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class Refund : BaseEntity, IAggregateRoot
{
    private Refund()
    {
    }

    public Refund(int orderId, string finixRefundId, long amountCents, string status, string idempotencyKey, DateTimeOffset createdAt)
    {
        OrderId = orderId;
        FinixRefundId = finixRefundId;
        AmountCents = amountCents;
        Status = status;
        IdempotencyKey = idempotencyKey;
        CreatedAt = createdAt;
    }

    public int OrderId { get; private set; }
    public string FinixRefundId { get; private set; } = string.Empty;
    public long AmountCents { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    public void UpdateStatus(string status)
    {
        Status = status;
    }
}
