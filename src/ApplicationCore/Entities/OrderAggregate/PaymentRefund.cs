using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single refund issued against a captured payment. Part of the <see cref="Order"/> aggregate,
/// owned by <see cref="OrderPayment"/>. Carries PayPal's own identifier and status so a later
/// request can reason about it, plus the caller-supplied idempotency key that produced it.
/// </summary>
public class PaymentRefund
{
    public string RefundId { get; private set; }
    public decimal Amount { get; private set; }
    public string Status { get; private set; }
    public string IdempotencyKey { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }
#pragma warning restore CS8618

    public PaymentRefund(string refundId, decimal amount, string status, string idempotencyKey, DateTimeOffset createdAt)
    {
        RefundId = refundId;
        Amount = amount;
        Status = status;
        IdempotencyKey = idempotencyKey;
        CreatedAt = createdAt;
    }
}
