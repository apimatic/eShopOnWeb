using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund taken against a captured payment. A capture can have several partial refunds,
/// each carrying the caller-supplied idempotency key that produced it so a repeated request under
/// the same key can be recognised rather than refunding twice.
/// </summary>
public class PaymentRefund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }

    public PaymentRefund(string idempotencyKey, decimal amount, string currencyCode, string? payPalRefundId, string status)
    {
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        CurrencyCode = currencyCode;
        PayPalRefundId = payPalRefundId;
        Status = status;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The caller-supplied idempotency key; unique per (order) capture.</summary>
    public string IdempotencyKey { get; private set; }

    public decimal Amount { get; private set; }
    public string CurrencyCode { get; private set; }

    /// <summary>PayPal's own id for this refund (the <c>refundId</c> returned to the caller).</summary>
    public string? PayPalRefundId { get; private set; }

    /// <summary>PayPal's refund status wire value (e.g. COMPLETED, PENDING).</summary>
    public string Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public void UpdateStatus(string status, string? payPalRefundId)
    {
        Status = status;
        if (payPalRefundId is not null)
        {
            PayPalRefundId = payPalRefundId;
        }
    }
}
