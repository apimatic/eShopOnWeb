using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// One refund against a captured payment. A capture may have several partial refunds; each carries the
/// caller-supplied idempotency key that produced it, so a repeat under the same key is recognised.
/// </summary>
public class OrderRefund : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderRefund() { }
#pragma warning restore CS8618

    public OrderRefund(string refundId, decimal amount, string status, string idempotencyKey)
    {
        RefundId = refundId;
        Amount = amount;
        Status = status;
        IdempotencyKey = idempotencyKey;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>PayPal-generated refund id.</summary>
    public string RefundId { get; private set; }

    public decimal Amount { get; private set; }

    /// <summary>PayPal refund status wire value (e.g. COMPLETED, PENDING).</summary>
    public string Status { get; private set; }

    /// <summary>Caller-supplied idempotency key that created this refund.</summary>
    public string IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>A refund counts against the refundable balance unless PayPal has cancelled/failed it.</summary>
    public bool CountsTowardRefunded =>
        !string.Equals(Status, "CANCELLED", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(Status, "FAILED", StringComparison.OrdinalIgnoreCase);
}
