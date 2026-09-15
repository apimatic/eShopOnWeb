using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund issued against a captured payment. A capture may be refunded more than
/// once (distinct partial refunds), so several of these can hang off one <see cref="Payment"/>.
/// The <see cref="IdempotencyKey"/> is the caller-supplied key that makes a repeated refund
/// request a no-op rather than a second money movement.
/// </summary>
public class PaymentRefund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }

    public PaymentRefund(string idempotencyKey, string payPalRefundId, decimal amount, string status)
    {
        IdempotencyKey = Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        PayPalRefundId = Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Amount = Guard.Against.NegativeOrZero(amount, nameof(amount));
        Status = Guard.Against.NullOrEmpty(status, nameof(status));
        CreatedDate = DateTimeOffset.UtcNow;
    }

    public int PaymentId { get; private set; }

    /// <summary>Caller-supplied idempotency key for this refund.</summary>
    public string IdempotencyKey { get; private set; }

    /// <summary>PayPal's own id for the refund.</summary>
    public string PayPalRefundId { get; private set; }

    public decimal Amount { get; private set; }

    /// <summary>PayPal refund status (e.g. COMPLETED, PENDING).</summary>
    public string Status { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }
}
