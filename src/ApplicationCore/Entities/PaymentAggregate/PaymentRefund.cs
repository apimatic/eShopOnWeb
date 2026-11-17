using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class PaymentRefund : BaseEntity, IAggregateRoot
{
    private PaymentRefund() { }

    public PaymentRefund(
        int orderId,
        string finixTransferId,
        string finixParentTransferId,
        long amountCents,
        string currency,
        string idempotencyKey,
        string? state)
    {
        Guard.Against.NullOrEmpty(finixTransferId, nameof(finixTransferId));
        Guard.Against.NullOrEmpty(finixParentTransferId, nameof(finixParentTransferId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        OrderId = orderId;
        FinixTransferId = finixTransferId;
        FinixParentTransferId = finixParentTransferId;
        AmountCents = amountCents;
        Currency = currency;
        IdempotencyKey = idempotencyKey;
        State = state;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string FinixTransferId { get; private set; }
    public string FinixParentTransferId { get; private set; }
    public long AmountCents { get; private set; }
    public string Currency { get; private set; }
    public string IdempotencyKey { get; private set; }
    public string? State { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
