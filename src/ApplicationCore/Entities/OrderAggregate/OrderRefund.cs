using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single refund issued against the captured payment of an <see cref="Order"/>. Owned by the Order
/// aggregate. <see cref="IdempotencyKey"/> is the caller-supplied key that makes repeat requests safe.
/// </summary>
public class OrderRefund
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderRefund() { }

    public OrderRefund(string payPalRefundId, decimal amount, string status, string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        PayPalRefundId = payPalRefundId;
        Amount = amount;
        Status = status;
        IdempotencyKey = idempotencyKey;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string PayPalRefundId { get; private set; }
    public decimal Amount { get; private set; }
    public string Status { get; private set; }

    /// <summary>The caller-supplied idempotency key under which this refund was requested.</summary>
    public string IdempotencyKey { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
