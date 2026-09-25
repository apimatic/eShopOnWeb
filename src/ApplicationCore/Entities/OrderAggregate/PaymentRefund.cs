using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single refund taken against the order's captured payment. Carries the PayPal-owned refund id
/// and status plus the caller-supplied idempotency key that produced it, so a repeated request under
/// the same key can be recognised rather than refunding twice.
/// </summary>
public class PaymentRefund
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }

    public PaymentRefund(string idempotencyKey, string payPalRefundId, decimal amount, string status)
    {
        IdempotencyKey = idempotencyKey;
        PayPalRefundId = payPalRefundId;
        Amount = amount;
        Status = status;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The caller-supplied idempotency key used for this refund (also sent to PayPal as PayPal-Request-Id).</summary>
    public string IdempotencyKey { get; private set; }

    /// <summary>PayPal's own id for the refund.</summary>
    public string PayPalRefundId { get; private set; }

    public decimal Amount { get; private set; }

    /// <summary>PayPal's current status for the refund (e.g. COMPLETED, PENDING).</summary>
    public string Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
