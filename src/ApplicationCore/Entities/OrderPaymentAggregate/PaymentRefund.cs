using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderPaymentAggregate;

/// <summary>
/// A single refund applied to a captured payment. Child of <see cref="OrderPayment"/>.
/// The <see cref="IdempotencyKey"/> is the caller-supplied key that makes a repeated refund
/// request a no-op while allowing distinct partial refunds under different keys.
/// </summary>
public class PaymentRefund : BaseEntity
{
    public string PayPalRefundId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public decimal Amount { get; private set; }
    public string Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }
#pragma warning restore CS8618

    public PaymentRefund(string payPalRefundId, string idempotencyKey, decimal amount, string status)
    {
        PayPalRefundId = payPalRefundId;
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        Status = status;
        CreatedAt = DateTimeOffset.UtcNow;
    }
}
