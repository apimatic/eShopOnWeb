using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A refund (Finix reversal) recorded against a captured order payment.
/// The caller supplied idempotency key is stored so a repeated request can be
/// answered without moving money twice.
/// </summary>
public class OrderRefund : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderRefund() { }
    #pragma warning restore CS8618

    public OrderRefund(
        int orderId,
        string? finixTransferId,
        long amountCents,
        string idempotencyKey,
        string state,
        string currency)
    {
        Guard.Against.Zero(orderId, nameof(orderId));
        Guard.Against.NegativeOrZero(amountCents, nameof(amountCents));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        OrderId = orderId;
        FinixTransferId = finixTransferId;
        AmountCents = amountCents;
        IdempotencyKey = idempotencyKey;
        State = state;
        Currency = currency;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string? FinixTransferId { get; private set; }
    public long AmountCents { get; private set; }
    public string IdempotencyKey { get; private set; }
    public string State { get; private set; }
    public string Currency { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
}

