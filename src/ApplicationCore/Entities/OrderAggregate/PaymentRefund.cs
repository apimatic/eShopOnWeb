using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single refund issued against the captured payment. Part of the Order aggregate (owned by
/// <see cref="PaymentRecord"/>). The caller-supplied <see cref="IdempotencyKey"/> guarantees a repeated
/// request under the same key does not refund twice, while distinct keys are distinct partial refunds.
/// </summary>
public class PaymentRefund // ValueObject owned by PaymentRecord
{
    public string PayPalRefundId { get; private set; }
    public string Status { get; private set; }
    public decimal Amount { get; private set; }
    public string IdempotencyKey { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }
#pragma warning restore CS8618

    public PaymentRefund(string payPalRefundId, string status, decimal amount, string idempotencyKey)
    {
        PayPalRefundId = payPalRefundId;
        Status = status;
        Amount = amount;
        IdempotencyKey = idempotencyKey;
        CreatedAt = DateTimeOffset.UtcNow;
    }
}
