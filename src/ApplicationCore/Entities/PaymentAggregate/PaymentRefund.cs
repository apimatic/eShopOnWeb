using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund issued against a captured payment. Carries the PayPal refund id and the
/// caller-supplied idempotency key so a repeated request under the same key is not refunded twice.
/// </summary>
public class PaymentRefund : BaseEntity
{
    public string IdempotencyKey { get; private set; }
    public string PayPalRefundId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public string Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

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
    }
}
