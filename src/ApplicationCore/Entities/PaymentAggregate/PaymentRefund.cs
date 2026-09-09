using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund taken against the captured payment of an order. Part of the
/// <see cref="OrderPayment"/> aggregate. The idempotency key is caller-supplied so a
/// replayed request maps back to the same refund instead of taking the money twice.
/// </summary>
public class PaymentRefund : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }
#pragma warning restore CS8618

    public PaymentRefund(string idempotencyKey, string payPalRefundId, decimal amount, string currencyCode, string status)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currencyCode, nameof(currencyCode));

        IdempotencyKey = idempotencyKey;
        PayPalRefundId = payPalRefundId;
        Amount = amount;
        CurrencyCode = currencyCode;
        Status = status;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Caller-supplied idempotency key, unique per distinct refund of a capture.</summary>
    public string IdempotencyKey { get; private set; }

    /// <summary>Identifier PayPal owns for this refund; used for later lookups.</summary>
    public string PayPalRefundId { get; private set; }

    public decimal Amount { get; private set; }
    public string CurrencyCode { get; private set; }

    /// <summary>Current PayPal status: COMPLETED, PENDING, CANCELLED, FAILED.</summary>
    public string Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public void UpdateStatus(string status) => Status = status;
}
