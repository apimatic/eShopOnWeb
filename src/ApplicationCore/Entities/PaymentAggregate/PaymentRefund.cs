using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund against a captured payment. The <see cref="IdempotencyKey"/> is the caller-supplied
/// key: a repeated request under the same key resolves to the same refund rather than issuing another.
/// </summary>
public class PaymentRefund : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }
#pragma warning restore CS8618

    public PaymentRefund(string idempotencyKey, string payPalRefundId, decimal amount, string currency, string status)
    {
        IdempotencyKey = idempotencyKey;
        PayPalRefundId = payPalRefundId;
        Amount = amount;
        Currency = currency;
        Status = status;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Caller-supplied idempotency key that produced this refund.</summary>
    public string IdempotencyKey { get; private set; }

    /// <summary>PayPal's refund id.</summary>
    public string PayPalRefundId { get; private set; }

    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public string Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
