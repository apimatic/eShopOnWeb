using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single refund issued against an order's capture. Multiple distinct partial refunds of
/// the same capture are legitimate; a repeat under the same <see cref="IdempotencyKey"/> is not,
/// and is deduplicated by the payment flow.
/// </summary>
public class PaymentRefund
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }
#pragma warning restore CS8618

    public PaymentRefund(string payPalRefundId, string status, decimal amount, string idempotencyKey)
    {
        PayPalRefundId = payPalRefundId;
        Status = status;
        Amount = amount;
        IdempotencyKey = idempotencyKey;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string PayPalRefundId { get; private set; }
    public string Status { get; private set; }
    public decimal Amount { get; private set; }

    /// <summary>Caller-supplied idempotency key that guards against double refunds.</summary>
    public string IdempotencyKey { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// A refund counts against the refundable balance unless PayPal explicitly failed/cancelled it.
    /// COMPLETED and PENDING both reserve the funds.
    /// </summary>
    public bool IsEffective =>
        !string.Equals(Status, "CANCELLED", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(Status, "FAILED", StringComparison.OrdinalIgnoreCase);
}
