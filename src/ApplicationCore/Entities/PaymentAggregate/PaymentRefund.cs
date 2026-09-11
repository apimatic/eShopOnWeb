using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>A single refund against a captured payment. Child of <see cref="OrderPayment"/>.</summary>
public class PaymentRefund : BaseEntity
{
    public string RefundId { get; private set; }
    public decimal Amount { get; private set; }
    public string Status { get; private set; }

    /// <summary>Caller-supplied idempotency key. A repeat under the same key returns this refund.</summary>
    public string IdempotencyKey { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }
#pragma warning restore CS8618

    public PaymentRefund(string refundId, decimal amount, string status, string idempotencyKey)
    {
        RefundId = refundId;
        Amount = amount;
        Status = status;
        IdempotencyKey = idempotencyKey;
        CreatedAt = DateTimeOffset.UtcNow;
    }
}
