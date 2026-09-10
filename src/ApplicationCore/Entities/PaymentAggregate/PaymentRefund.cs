using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund taken against an order's captured payment. Holds the caller-supplied idempotency key
/// so a repeated request under the same key is recognised and never refunds twice.
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

    public int OrderPaymentId { get; private set; }

    /// <summary>Caller-supplied idempotency key that produced this refund.</summary>
    public string IdempotencyKey { get; private set; }

    /// <summary>PayPal-generated refund id.</summary>
    public string PayPalRefundId { get; private set; }

    public decimal Amount { get; private set; }

    /// <summary>PayPal refund status (wire value, e.g. COMPLETED/PENDING).</summary>
    public string Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
