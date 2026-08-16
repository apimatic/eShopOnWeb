using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single refund issued against the order's captured payment. Part of the Order
/// aggregate (owned collection) — refunds are never manipulated outside the aggregate root.
/// </summary>
public class OrderRefund
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderRefund() { }
#pragma warning restore CS8618

    public OrderRefund(string refundId, decimal amount, string status, string idempotencyKey)
    {
        RefundId = Guard.Against.NullOrEmpty(refundId, nameof(refundId));
        Amount = Guard.Against.NegativeOrZero(amount, nameof(amount));
        Status = Guard.Against.NullOrEmpty(status, nameof(status));
        IdempotencyKey = Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        CreatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>PayPal's own refund id (from the Payments API).</summary>
    public string RefundId { get; private set; }

    /// <summary>The refunded amount.</summary>
    public decimal Amount { get; private set; }

    /// <summary>PayPal's current status for the refund (e.g. COMPLETED, PENDING).</summary>
    public string Status { get; private set; }

    /// <summary>Caller-supplied idempotency key that produced this refund.</summary>
    public string IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }
}
