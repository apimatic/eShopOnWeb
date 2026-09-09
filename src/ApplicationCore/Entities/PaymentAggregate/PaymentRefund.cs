using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund taken against a captured payment. Child of <see cref="OrderPayment"/>.
/// The <see cref="IdempotencyKey"/> is the caller-supplied key that made the refund request
/// idempotent — a repeat under the same key returns this record instead of refunding again.
/// </summary>
public class PaymentRefund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }
    #pragma warning restore CS8618

    public PaymentRefund(string refundId, decimal amount, string idempotencyKey, string status)
    {
        RefundId = refundId;
        Amount = amount;
        IdempotencyKey = idempotencyKey;
        Status = status;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>PayPal-generated refund id.</summary>
    public string RefundId { get; private set; }
    public decimal Amount { get; private set; }
    public string IdempotencyKey { get; private set; }
    public string Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
