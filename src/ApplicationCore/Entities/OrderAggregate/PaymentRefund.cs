using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single refund issued against the order's capture. Part of the Order aggregate (owned by
/// <see cref="Payment"/>). Carries the PayPal refund id and the caller-supplied idempotency key
/// so a repeat of the same refund request can be recognised and replayed rather than refunded twice.
/// </summary>
public class PaymentRefund
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }

    public PaymentRefund(string idempotencyKey, string payPalRefundId, string status, decimal amount, DateTimeOffset createdAt)
    {
        IdempotencyKey = idempotencyKey;
        PayPalRefundId = payPalRefundId;
        Status = status;
        Amount = amount;
        CreatedAt = createdAt;
    }

    /// <summary>Caller-supplied key that makes the refund request idempotent.</summary>
    public string IdempotencyKey { get; private set; }

    /// <summary>PayPal's refund id.</summary>
    public string PayPalRefundId { get; private set; }

    /// <summary>PayPal refund status (e.g. COMPLETED, PENDING).</summary>
    public string Status { get; private set; }

    public decimal Amount { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>A refund that failed does not consume any of the captured amount.</summary>
    public bool CountsAgainstCapture =>
        !string.Equals(Status, "FAILED", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(Status, "CANCELLED", StringComparison.OrdinalIgnoreCase);
}
