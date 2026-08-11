using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund taken against a captured payment. A refund is uniquely identified for idempotency
/// by the caller-supplied <see cref="IdempotencyKey"/>: repeating a request under the same key must not
/// refund twice, while two distinct partial refunds of the same capture remain legitimate.
/// </summary>
public class PaymentRefund : BaseEntity
{
    /// <summary>PayPal's own id for this refund.</summary>
    public string RefundId { get; private set; }

    /// <summary>The amount refunded, in the payment currency.</summary>
    public decimal Amount { get; private set; }

    /// <summary>PayPal's current status for the refund (e.g. COMPLETED, PENDING, FAILED, CANCELLED).</summary>
    public string Status { get; private set; }

    /// <summary>The caller-supplied idempotency key that produced this refund.</summary>
    public string IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }
#pragma warning restore CS8618

    public PaymentRefund(string refundId, decimal amount, string status, string idempotencyKey)
    {
        RefundId = refundId;
        Amount = amount;
        Status = status;
        IdempotencyKey = idempotencyKey;
    }

    /// <summary>Whether this refund counts against the captured amount (i.e. it was not rejected).</summary>
    public bool CountsTowardRefunded() =>
        !string.Equals(Status, "FAILED", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(Status, "CANCELLED", StringComparison.OrdinalIgnoreCase);
}
