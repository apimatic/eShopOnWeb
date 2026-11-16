using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class PaymentRefund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }

    public PaymentRefund(
        int orderPaymentId,
        string idempotencyKey,
        long amountCents,
        string finixReversalTransferId,
        string? state)
    {
        Guard.Against.NegativeOrZero(orderPaymentId, nameof(orderPaymentId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amountCents, nameof(amountCents));
        Guard.Against.NullOrEmpty(finixReversalTransferId, nameof(finixReversalTransferId));

        OrderPaymentId = orderPaymentId;
        IdempotencyKey = idempotencyKey;
        AmountCents = amountCents;
        FinixReversalTransferId = finixReversalTransferId;
        State = state;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderPaymentId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public long AmountCents { get; private set; }
    public string FinixReversalTransferId { get; private set; }
    public string? State { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
