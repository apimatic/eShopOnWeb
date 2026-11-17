using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A refund (Finix reversal transfer) issued against a captured order payment.
/// </summary>
public class OrderRefund : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderRefund() { }

    public OrderRefund(
        int orderId,
        string finixRefundId,
        decimal amount,
        string currency,
        string state,
        string idempotencyKey)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrWhiteSpace(finixRefundId, nameof(finixRefundId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrWhiteSpace(currency, nameof(currency));
        Guard.Against.NullOrWhiteSpace(state, nameof(state));
        Guard.Against.NullOrWhiteSpace(idempotencyKey, nameof(idempotencyKey));

        OrderId = orderId;
        FinixRefundId = finixRefundId;
        Amount = amount;
        Currency = currency;
        State = state;
        IdempotencyKey = idempotencyKey;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public int OrderId { get; private set; }
    public string FinixRefundId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public string State { get; private set; }
    public string IdempotencyKey { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void UpdateState(string state)
    {
        Guard.Against.NullOrWhiteSpace(state, nameof(state));
        State = state;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
