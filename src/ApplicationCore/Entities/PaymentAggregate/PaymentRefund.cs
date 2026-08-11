using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund applied against a captured payment. Several partial refunds may exist for one
/// <see cref="Payment"/>. The <see cref="IdempotencyKey"/> is the caller-supplied key that makes a
/// repeated refund request a no-op rather than a second refund.
/// </summary>
public class PaymentRefund : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }
#pragma warning restore CS8618

    public PaymentRefund(string idempotencyKey, string refundId, decimal amount, string currency, string status)
    {
        IdempotencyKey = idempotencyKey;
        RefundId = refundId;
        Amount = amount;
        Currency = currency;
        Status = status;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Caller-supplied idempotency key (also sent to PayPal as PayPal-Request-Id).</summary>
    public string IdempotencyKey { get; private set; }

    /// <summary>PayPal's refund id.</summary>
    public string RefundId { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; }

    /// <summary>PayPal's refund status (e.g. COMPLETED, PENDING).</summary>
    public string Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>A refund still counts against the captured total unless PayPal outright failed it.</summary>
    public bool CountsTowardRefundedTotal =>
        !string.Equals(Status, "FAILED", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(Status, "CANCELLED", StringComparison.OrdinalIgnoreCase);
}
