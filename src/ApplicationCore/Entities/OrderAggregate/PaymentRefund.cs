using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single refund issued against the captured payment. Owned by <see cref="OrderPayment"/>.
/// <see cref="IdempotencyKey"/> is the caller-supplied key that makes repeat requests safe:
/// the same key must never refund twice, but two distinct keys are two legitimate partial refunds.
/// </summary>
public class PaymentRefund
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }

    public PaymentRefund(string payPalRefundId, decimal amount, string status, string idempotencyKey)
    {
        PayPalRefundId = payPalRefundId;
        Amount = amount;
        Status = status;
        IdempotencyKey = idempotencyKey;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string PayPalRefundId { get; private set; }
    public decimal Amount { get; private set; }
    public string Status { get; private set; }
    public string IdempotencyKey { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
