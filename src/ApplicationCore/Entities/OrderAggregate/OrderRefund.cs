using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single refund issued against the order's captured payment. Part of the Order aggregate,
/// persisted as an owned collection under <see cref="OrderPayment"/>.
/// </summary>
public class OrderRefund
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderRefund() { }
#pragma warning restore CS8618

    public OrderRefund(string refundId, string idempotencyKey, decimal amount, string status, DateTimeOffset createdAt)
    {
        RefundId = refundId;
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        Status = status;
        CreatedAt = createdAt;
    }

    /// <summary>PayPal's refund id.</summary>
    public string RefundId { get; private set; }

    /// <summary>Caller-supplied idempotency key that produced this refund.</summary>
    public string IdempotencyKey { get; private set; }

    public decimal Amount { get; private set; }

    /// <summary>PayPal refund status (e.g. COMPLETED, PENDING, FAILED, CANCELLED).</summary>
    public string Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>A refund counts against the captured total unless PayPal rejected/cancelled it.</summary>
    public bool CountsAgainstCapture =>
        !string.Equals(Status, "FAILED", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(Status, "CANCELLED", StringComparison.OrdinalIgnoreCase);
}
