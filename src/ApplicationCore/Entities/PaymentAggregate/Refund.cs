using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class Refund : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private Refund() { }

    public Refund(
        int paymentId,
        int orderId,
        string ownerId,
        string idempotencyKey,
        long amountCents,
        string finixTransferId,
        string state,
        DateTimeOffset createdAt)
    {
        Guard.Against.NegativeOrZero(paymentId, nameof(paymentId));
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amountCents, nameof(amountCents));
        Guard.Against.NullOrEmpty(finixTransferId, nameof(finixTransferId));
        Guard.Against.NullOrEmpty(state, nameof(state));

        PaymentId = paymentId;
        OrderId = orderId;
        OwnerId = ownerId;
        IdempotencyKey = idempotencyKey;
        AmountCents = amountCents;
        FinixTransferId = finixTransferId;
        State = state;
        CreatedAt = createdAt;
    }

    public int PaymentId { get; private set; }
    public int OrderId { get; private set; }
    public string OwnerId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public long AmountCents { get; private set; }
    public string FinixTransferId { get; private set; }
    public string State { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void UpdateState(string state)
    {
        if (!string.IsNullOrWhiteSpace(state))
        {
            State = state;
        }
    }
}
