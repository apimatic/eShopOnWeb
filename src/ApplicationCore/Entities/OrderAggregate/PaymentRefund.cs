using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single refund issued against the captured payment. Refunds may be full or partial; several
/// distinct partial refunds can exist for one capture. The <see cref="IdempotencyKey"/> is the
/// caller-supplied key that makes a repeated refund request return the same refund rather than
/// issuing a second one.
/// </summary>
public class PaymentRefund : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }
#pragma warning restore CS8618

    public PaymentRefund(string idempotencyKey, string payPalRefundId, decimal amount, string status)
    {
        IdempotencyKey = idempotencyKey;
        PayPalRefundId = payPalRefundId;
        Amount = amount;
        Status = status;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Caller-supplied idempotency key for the refund request.</summary>
    public string IdempotencyKey { get; private set; }

    /// <summary>PayPal-generated refund id.</summary>
    public string PayPalRefundId { get; private set; }

    /// <summary>The amount refunded, in the payment currency.</summary>
    public decimal Amount { get; private set; }

    /// <summary>PayPal refund status (e.g. COMPLETED, PENDING).</summary>
    public string Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
