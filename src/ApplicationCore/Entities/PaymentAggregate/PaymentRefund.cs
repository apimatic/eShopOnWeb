using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund issued against a captured payment. A capture can have several distinct partial
/// refunds; each carries the caller-supplied idempotency key that guards against a repeat refunding twice.
/// </summary>
public class PaymentRefund : BaseEntity
{
    public int OrderPaymentId { get; private set; }

    /// <summary>Caller-supplied idempotency key. Unique per logical refund request.</summary>
    public string IdempotencyKey { get; private set; }

    public decimal Amount { get; private set; }

    /// <summary>PayPal's refund id, once the refund has been accepted.</summary>
    public string? PayPalRefundId { get; private set; }

    /// <summary>PayPal refund status: COMPLETED, PENDING, CANCELLED, FAILED (or FAILED_LOCAL before the call).</summary>
    public string Status { get; private set; }

    /// <summary>The running total refunded against the capture, as reported by PayPal.</summary>
    public decimal? TotalRefundedAmount { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }
#pragma warning restore CS8618

    public PaymentRefund(string idempotencyKey, decimal amount)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        Status = "PENDING_LOCAL";
    }

    /// <summary>Records the outcome PayPal reported for this refund.</summary>
    public void SetResult(string payPalRefundId, string status, decimal? totalRefundedAmount)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Guard.Against.NullOrEmpty(status, nameof(status));
        PayPalRefundId = payPalRefundId;
        Status = status;
        TotalRefundedAmount = totalRefundedAmount;
    }

    public void MarkFailed()
    {
        Status = "FAILED_LOCAL";
    }

    /// <summary>A refund still counts against the capture unless it explicitly failed or was cancelled.</summary>
    public bool CountsAgainstCapture =>
        !string.Equals(Status, "FAILED", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(Status, "FAILED_LOCAL", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(Status, "CANCELLED", StringComparison.OrdinalIgnoreCase);
}
