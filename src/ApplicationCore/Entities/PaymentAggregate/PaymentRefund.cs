using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class PaymentRefund : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }
#pragma warning restore CS8618

    public PaymentRefund(int orderPaymentId, long amountInCents, string idempotencyKey,
        string? finixRefundId, string? state)
    {
        OrderPaymentId = orderPaymentId;
        AmountInCents = amountInCents;
        IdempotencyKey = idempotencyKey;
        FinixRefundId = finixRefundId;
        State = state;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderPaymentId { get; private set; }
    public long AmountInCents { get; private set; }
    public string IdempotencyKey { get; private set; }
    public string? FinixRefundId { get; private set; }
    public string? State { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
