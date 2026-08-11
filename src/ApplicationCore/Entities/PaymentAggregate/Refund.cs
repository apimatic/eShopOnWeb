using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund issued against a captured payment. Refunds are children of the
/// <see cref="Payment"/> aggregate. The <see cref="IdempotencyKey"/> is the caller-supplied
/// key that makes repeating the same refund request safe; <see cref="PayPalRefundId"/> is the
/// refund's business identity, returned to callers.
/// </summary>
public class Refund : BaseEntity
{
    public string IdempotencyKey { get; private set; }

    /// <summary>The PayPal-generated refund id.</summary>
    public string PayPalRefundId { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; }

    /// <summary>The status PayPal reported for the refund (e.g. COMPLETED, PENDING).</summary>
    public string Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private Refund() { }
#pragma warning restore CS8618

    public Refund(string idempotencyKey, string payPalRefundId, decimal amount, string currency, string status)
    {
        IdempotencyKey = idempotencyKey;
        PayPalRefundId = payPalRefundId;
        Amount = amount;
        Currency = currency;
        Status = status;
    }
}
