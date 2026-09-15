using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund taken against a captured payment. Part of the <see cref="Payment"/> aggregate.
/// Multiple distinct partial refunds are legitimate; a repeat under the same
/// <see cref="IdempotencyKey"/> is not and is de-duplicated by the application.
/// </summary>
public class PaymentRefund
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }

    public PaymentRefund(Guid id, decimal amount, string idempotencyKey, string? payPalRefundId, string status)
    {
        Id = id;
        Amount = amount;
        IdempotencyKey = idempotencyKey;
        PayPalRefundId = payPalRefundId;
        Status = status;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Our identifier for the refund, surfaced to callers as <c>refundId</c>.</summary>
    public Guid Id { get; private set; }

    /// <summary>The caller-supplied idempotency key that produced this refund.</summary>
    public string IdempotencyKey { get; private set; }

    /// <summary>The amount returned to the shopper.</summary>
    public decimal Amount { get; private set; }

    /// <summary>PayPal's own identifier for the refund.</summary>
    public string? PayPalRefundId { get; private set; }

    /// <summary>PayPal's reported refund status (e.g. COMPLETED, PENDING).</summary>
    public string Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
