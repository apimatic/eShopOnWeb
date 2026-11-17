using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class PaymentRefund : BaseEntity
{
#pragma warning disable CS8618
    private PaymentRefund() { }
#pragma warning restore CS8618

    public PaymentRefund(int paymentId, string idempotencyKey, long amountCents, string finixTransferId, string? state)
    {
        PaymentId = paymentId;
        IdempotencyKey = idempotencyKey;
        AmountCents = amountCents;
        FinixTransferId = finixTransferId;
        FinixState = state;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int PaymentId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public long AmountCents { get; private set; }
    public string? FinixTransferId { get; private set; }
    public string? FinixState { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
