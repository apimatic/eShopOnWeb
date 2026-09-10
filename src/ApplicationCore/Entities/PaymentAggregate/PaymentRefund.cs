using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund taken against an order's captured payment. Each carries the caller-supplied idempotency
/// key so a repeated request under the same key is recognised and never refunds twice, while two distinct
/// keys represent two legitimate partial refunds.
/// </summary>
public class PaymentRefund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }

    public PaymentRefund(string idempotencyKey, decimal amount, string? payPalRefundId, string? status)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        IdempotencyKey = idempotencyKey;
        Amount = amount;
        PayPalRefundId = payPalRefundId;
        Status = status;
        CreatedDate = System.DateTimeOffset.UtcNow;
    }

    public int OrderPaymentId { get; private set; }

    /// <summary>Caller-supplied idempotency key that produced this refund.</summary>
    public string IdempotencyKey { get; private set; }

    /// <summary>The refunded amount, in the payment's currency.</summary>
    public decimal Amount { get; private set; }

    /// <summary>PayPal's id for the refund.</summary>
    public string? PayPalRefundId { get; private set; }

    /// <summary>PayPal's reported refund status (e.g. COMPLETED, PENDING).</summary>
    public string? Status { get; private set; }

    public System.DateTimeOffset CreatedDate { get; private set; }
}
