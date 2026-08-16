using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single refund issued against a captured <see cref="Payment"/>. The
/// <see cref="IdempotencyKey"/> is the caller-supplied key that makes repeat requests safe:
/// two distinct partial refunds carry two distinct keys, a retry carries the same one.
/// </summary>
public class PaymentRefund : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }
#pragma warning restore CS8618

    public PaymentRefund(string payPalRefundId, decimal amount, string status, string idempotencyKey)
    {
        PayPalRefundId = payPalRefundId;
        Amount = amount;
        Status = status;
        IdempotencyKey = idempotencyKey;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>PayPal's own identifier for this refund.</summary>
    public string PayPalRefundId { get; private set; }

    public decimal Amount { get; private set; }

    /// <summary>PayPal refund status (e.g. COMPLETED, PENDING).</summary>
    public string Status { get; private set; }

    /// <summary>Caller-supplied idempotency key that this refund was created under.</summary>
    public string IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>A refund that PayPal has accepted (counts against the refundable balance).</summary>
    public bool CountsAgainstCapture => !string.Equals(Status, "FAILED", StringComparison.OrdinalIgnoreCase)
                                        && !string.Equals(Status, "CANCELLED", StringComparison.OrdinalIgnoreCase);
}
