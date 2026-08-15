using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single refund taken against an order's captured payment. Part of the <see cref="Order"/>
/// aggregate (child entity, not an aggregate root). An order can have several partial refunds, as
/// long as their total never exceeds what was captured.
/// </summary>
public class OrderRefund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderRefund() { }

    public OrderRefund(string idempotencyKey, string payPalRefundId, decimal amount, string status)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        IdempotencyKey = idempotencyKey;
        PayPalRefundId = payPalRefundId;
        Amount = amount;
        Status = status;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The caller-supplied idempotency key. Repeating a request under the same key must
    /// not refund twice — the existing refund is returned instead.</summary>
    public string IdempotencyKey { get; private set; }

    /// <summary>PayPal's own id for this refund.</summary>
    public string PayPalRefundId { get; private set; }

    public decimal Amount { get; private set; }

    /// <summary>PayPal refund status (e.g. COMPLETED, PENDING).</summary>
    public string Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
