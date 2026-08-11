using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund issued against the captured payment of an order. Refunds are children of the
/// <see cref="OrderPayment"/> aggregate. <see cref="IdempotencyKey"/> is supplied by the caller so a
/// repeated request under the same key is not refunded twice, while two distinct keys are two
/// legitimate partial refunds of the same capture.
/// </summary>
public class PaymentRefund : BaseEntity
{
    public int OrderPaymentId { get; private set; }

    /// <summary>PayPal-generated id for the refund (from POST /v2/payments/captures/{id}/refund).</summary>
    public string PayPalRefundId { get; private set; }

    public decimal Amount { get; private set; }

    public string CurrencyCode { get; private set; }

    /// <summary>PayPal refund status: COMPLETED, PENDING, CANCELLED, FAILED.</summary>
    public string Status { get; private set; }

    /// <summary>Caller-supplied idempotency key that guards against double-refunding.</summary>
    public string IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }
#pragma warning restore CS8618

    public PaymentRefund(string payPalRefundId, decimal amount, string currencyCode, string status, string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));
        Guard.Against.NullOrEmpty(status, nameof(status));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        PayPalRefundId = payPalRefundId;
        Amount = amount;
        CurrencyCode = currencyCode;
        Status = status;
        IdempotencyKey = idempotencyKey;
    }
}
