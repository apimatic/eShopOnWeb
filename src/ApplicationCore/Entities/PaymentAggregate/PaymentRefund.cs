using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund taken against a captured <see cref="Payment"/>. Refunds are part of the Payment
/// aggregate. The caller-supplied <see cref="IdempotencyKey"/> makes repeating a refund request a no-op
/// while still allowing two distinct partial refunds of the same capture.
/// </summary>
public class PaymentRefund
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }

    public PaymentRefund(string idempotencyKey, decimal amount, string payPalRefundId, string status)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Guard.Against.NullOrEmpty(status, nameof(status));

        Id = Guid.NewGuid().ToString("N");
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        PayPalRefundId = payPalRefundId;
        Status = status;
        CreatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>Local identity of the refund row (owned-entity key).</summary>
    public string Id { get; private set; }

    /// <summary>The caller-supplied idempotency key that produced this refund.</summary>
    public string IdempotencyKey { get; private set; }

    /// <summary>The refunded amount.</summary>
    public decimal Amount { get; private set; }

    /// <summary>PayPal's own refund id — the value returned to the caller and used for reconciliation.</summary>
    public string PayPalRefundId { get; private set; }

    /// <summary>PayPal's refund status (e.g. COMPLETED, PENDING).</summary>
    public string Status { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }
}
