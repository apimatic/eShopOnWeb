using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

/// <summary>
/// A single refund against an order's captured payment. The <see cref="IdempotencyKey"/> is the
/// caller-supplied key: repeating a refund request under the same key returns the same record rather than
/// refunding twice, while two distinct keys are two legitimate partial refunds.
/// </summary>
public class PaymentRefund : BaseEntity
{
    public int OrderPaymentId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public decimal Amount { get; private set; }
    public string? PayPalRefundId { get; private set; }
    public string? Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }
#pragma warning restore CS8618

    public PaymentRefund(string idempotencyKey, decimal amount)
    {
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        Status = "PENDING";
    }

    public void MarkResult(string? payPalRefundId, string? status)
    {
        PayPalRefundId = payPalRefundId;
        Status = status;
    }

    public void MarkFailed() => Status = "FAILED";

    /// <summary>A refund still reserves the captured amount unless it failed or was cancelled.</summary>
    public bool CountsAgainstCapture =>
        !string.Equals(Status, "FAILED", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(Status, "CANCELLED", StringComparison.OrdinalIgnoreCase);
}
