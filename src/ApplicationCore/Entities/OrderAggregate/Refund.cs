using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single refund issued against a captured <see cref="Payment"/>. A capture may be refunded
/// in full or across several partial refunds; each carries the caller-supplied idempotency key
/// so a repeated request never refunds twice.
/// </summary>
public class Refund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Refund() { }

    public Refund(string payPalRefundId, decimal amount, string status, string idempotencyKey)
    {
        PayPalRefundId = Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Amount = amount;
        Status = status;
        IdempotencyKey = Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>PayPal's own id for this refund (from the refund capture call).</summary>
    public string PayPalRefundId { get; private set; }

    /// <summary>Amount refunded by this refund, in the payment currency.</summary>
    public decimal Amount { get; private set; }

    /// <summary>PayPal's reported status for the refund (e.g. COMPLETED, PENDING).</summary>
    public string Status { get; private set; }

    /// <summary>Caller-supplied idempotency key; also sent to PayPal as PayPal-Request-Id.</summary>
    public string IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public void UpdateStatus(string status) => Status = status;
}
