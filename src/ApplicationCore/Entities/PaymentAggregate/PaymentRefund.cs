using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class PaymentRefund : BaseEntity, IAggregateRoot
{
    public int PaymentId { get; private set; }
    public int OrderId { get; private set; }
    public string FinixRefundTransferId { get; private set; }
    public long AmountCents { get; private set; }
    public string State { get; private set; }
    public string IdempotencyKey { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    private PaymentRefund()
    {
        FinixRefundTransferId = string.Empty;
        State = string.Empty;
        IdempotencyKey = string.Empty;
    }

    public PaymentRefund(int paymentId, int orderId, string finixRefundTransferId, long amountCents,
        string state, string idempotencyKey)
    {
        PaymentId = paymentId;
        OrderId = orderId;
        FinixRefundTransferId = finixRefundTransferId;
        AmountCents = amountCents;
        State = state;
        IdempotencyKey = idempotencyKey;
    }
}
