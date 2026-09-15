using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single refund issued against a captured <see cref="Payment"/>. Two distinct partial
/// refunds of the same capture are legitimate; a repeat under the same idempotency key is not.
/// </summary>
public class PaymentRefund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }

    public PaymentRefund(string idempotencyKey, decimal amount, string refundId, string status)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(refundId, nameof(refundId));

        IdempotencyKey = idempotencyKey;
        Amount = amount;
        RefundId = refundId;
        Status = status;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Caller-supplied key that makes repeating a refund request a no-op.</summary>
    public string IdempotencyKey { get; private set; }

    /// <summary>Amount returned to the shopper for this refund.</summary>
    public decimal Amount { get; private set; }

    /// <summary>PayPal's refund id.</summary>
    public string RefundId { get; private set; }

    /// <summary>PayPal's current status for this refund (e.g. COMPLETED, PENDING).</summary>
    public string Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
