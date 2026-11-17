using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A Finix reversal (refund) of a captured transfer.
/// </summary>
public class PaymentRefund : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }
#pragma warning restore CS8618

    public PaymentRefund(
        int orderPaymentId,
        string finixTransferId,
        string idempotencyKey,
        long amountInCents,
        string state)
    {
        OrderPaymentId = orderPaymentId;
        FinixTransferId = finixTransferId;
        IdempotencyKey = idempotencyKey;
        AmountInCents = amountInCents;
        State = state;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderPaymentId { get; private set; }
    public string FinixTransferId { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public long AmountInCents { get; private set; }
    public string State { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }

    public void UpdateState(string state)
    {
        State = state;
    }
}
