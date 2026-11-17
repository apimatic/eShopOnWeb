using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class Refund : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Refund() { }

    public Refund(int paymentId,
        string finixRefundId,
        string parentTransferId,
        int amountCents,
        string state,
        string idempotencyKey)
    {
        Guard.Against.NegativeOrZero(paymentId, nameof(paymentId));
        Guard.Against.NullOrEmpty(finixRefundId, nameof(finixRefundId));
        Guard.Against.NullOrEmpty(parentTransferId, nameof(parentTransferId));
        Guard.Against.NegativeOrZero(amountCents, nameof(amountCents));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        PaymentId = paymentId;
        FinixRefundId = finixRefundId;
        ParentTransferId = parentTransferId;
        AmountCents = amountCents;
        State = state;
        IdempotencyKey = idempotencyKey;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int PaymentId { get; private set; }
    public string FinixRefundId { get; private set; }
    public string ParentTransferId { get; private set; }
    public int AmountCents { get; private set; }
    public string State { get; private set; }
    public string IdempotencyKey { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void UpdateState(string state)
    {
        State = state;
    }
}
