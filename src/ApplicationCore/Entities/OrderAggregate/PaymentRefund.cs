using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single refund against a captured payment. Belongs to the <see cref="OrderPayment"/> and is created only
/// through <see cref="OrderPayment.AddRefund"/>. The <see cref="IdempotencyKey"/> is the caller-supplied key
/// that makes repeating a refund request under the same key a no-op.
/// </summary>
public class PaymentRefund : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }
#pragma warning restore CS8618

    public PaymentRefund(string payPalRefundId, string status, decimal amount, string currency, string idempotencyKey)
    {
        PayPalRefundId = payPalRefundId;
        Status = status;
        Amount = amount;
        Currency = currency;
        IdempotencyKey = idempotencyKey;
        RefundedAt = DateTimeOffset.Now;
    }

    /// <summary>PayPal's own id for this refund.</summary>
    public string PayPalRefundId { get; private set; }
    public string Status { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }

    /// <summary>Caller-supplied idempotency key that authorised this refund.</summary>
    public string IdempotencyKey { get; private set; }
    public DateTimeOffset RefundedAt { get; private set; }
}
