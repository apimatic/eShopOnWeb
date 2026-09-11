using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single refund taken against the captured payment of an <see cref="OrderPayment"/>.
/// Refunds are keyed by a caller-supplied idempotency key so that repeating the same
/// request never issues the money back twice, while two genuinely distinct partial
/// refunds of the same capture remain separate records.
/// </summary>
public class PaymentRefund : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }
#pragma warning restore CS8618

    public PaymentRefund(string idempotencyKey, decimal amount, string currencyCode)
    {
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        CurrencyCode = currencyCode;
        Status = "PENDING";
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Caller-supplied key that makes the refund request idempotent.</summary>
    public string IdempotencyKey { get; private set; }

    /// <summary>The PayPal-generated refund id, once PayPal has accepted the refund.</summary>
    public string? PayPalRefundId { get; private set; }

    /// <summary>The amount returned to the payer.</summary>
    public decimal Amount { get; private set; }

    public string CurrencyCode { get; private set; }

    /// <summary>PayPal's current status for the refund (e.g. COMPLETED, PENDING).</summary>
    public string Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public void MarkCompleted(string payPalRefundId, string status)
    {
        PayPalRefundId = payPalRefundId;
        Status = status;
    }
}
