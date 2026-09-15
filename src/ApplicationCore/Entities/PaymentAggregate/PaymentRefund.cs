using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund issued against a captured <see cref="Payment"/>. Carries PayPal's refund id and
/// status plus the caller-supplied idempotency key, so a repeated request under the same key can be
/// recognised and never refunds twice, while two distinct partial refunds remain legitimate.
/// </summary>
public class PaymentRefund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }

    public PaymentRefund(string refundId, decimal amount, string status, string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(refundId, nameof(refundId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(status, nameof(status));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        RefundId = refundId;
        Amount = amount;
        Status = status;
        IdempotencyKey = idempotencyKey;
    }

    /// <summary>PayPal's refund id.</summary>
    public string RefundId { get; private set; }

    /// <summary>The refunded amount.</summary>
    public decimal Amount { get; private set; }

    /// <summary>PayPal's refund status (e.g. COMPLETED, PENDING).</summary>
    public string Status { get; private set; }

    /// <summary>The caller-supplied idempotency key this refund was created under.</summary>
    public string IdempotencyKey { get; private set; }

    public void UpdateStatus(string status)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        Status = status;
    }

    /// <summary>
    /// A refund still counts against the captured total unless PayPal explicitly failed or cancelled it.
    /// </summary>
    public bool CountsAgainstCapture =>
        !string.Equals(Status, "FAILED", System.StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(Status, "CANCELLED", System.StringComparison.OrdinalIgnoreCase);
}
