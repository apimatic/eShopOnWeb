using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund taken against a captured payment. A payment may have several
/// (partial refunds); each carries the caller-supplied idempotency key so a repeat
/// under the same key is recognised and never refunds twice.
/// </summary>
public class PaymentRefund : BaseEntity
{
    public int PaymentId { get; private set; }

    /// <summary>PayPal's refund id (from POST /v2/payments/captures/{id}/refund).</summary>
    public string PayPalRefundId { get; private set; }

    /// <summary>Caller-supplied idempotency key that produced this refund.</summary>
    public string IdempotencyKey { get; private set; }

    public decimal Amount { get; private set; }
    public string CurrencyCode { get; private set; }

    /// <summary>PayPal refund status, e.g. COMPLETED / PENDING.</summary>
    public string Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }
#pragma warning restore CS8618

    public PaymentRefund(string payPalRefundId, string idempotencyKey, decimal amount, string currencyCode, string status)
    {
        PayPalRefundId = payPalRefundId;
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        CurrencyCode = currencyCode;
        Status = status;
    }
}
