using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund taken against a captured <see cref="OrderPayment"/>. Each refund carries the
/// caller-supplied idempotency key that produced it, so a repeated request under the same key is
/// recognised and never refunds twice, while two distinct partial refunds remain separate rows.
/// </summary>
public class PaymentRefund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }
    #pragma warning restore CS8618

    public PaymentRefund(string idempotencyKey, decimal amount, string currencyCode)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));

        IdempotencyKey = idempotencyKey;
        Amount = amount;
        CurrencyCode = currencyCode;
        CreatedDate = DateTimeOffset.Now;
    }

    public int OrderPaymentId { get; private set; }

    /// <summary>The caller-supplied idempotency key that requested this refund.</summary>
    public string IdempotencyKey { get; private set; }

    public decimal Amount { get; private set; }
    public string CurrencyCode { get; private set; }

    /// <summary>PayPal's own refund id (null until PayPal has accepted the refund).</summary>
    public string? PayPalRefundId { get; private set; }

    /// <summary>PayPal's current status for the refund (e.g. COMPLETED, PENDING).</summary>
    public string? Status { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }

    public void SetResult(string payPalRefundId, string? status)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        PayPalRefundId = payPalRefundId;
        Status = status;
    }
}
