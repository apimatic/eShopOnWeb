using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single refund against the captured payment of an <see cref="OrderPayment"/>.
/// Carries the caller-supplied idempotency key so a repeat under the same key is not applied twice,
/// while two distinct keys remain two legitimate partial refunds.
/// </summary>
public class PaymentRefund : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }
#pragma warning restore CS8618

    public PaymentRefund(string idempotencyKey, decimal amount)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        IdempotencyKey = idempotencyKey;
        Amount = amount;
        Status = "PENDING";
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Caller-supplied key that makes this refund request idempotent at PayPal and locally.</summary>
    public string IdempotencyKey { get; private set; }
    public decimal Amount { get; private set; }
    public string Status { get; private set; }
    public string? PayPalRefundId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void SetResult(string payPalRefundId, string status)
    {
        PayPalRefundId = payPalRefundId;
        Status = status;
    }

    /// <summary>A refund counts against the captured total unless PayPal explicitly cancelled/failed it.</summary>
    public bool CountsAgainstCapture =>
        !string.Equals(Status, "CANCELLED", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(Status, "FAILED", StringComparison.OrdinalIgnoreCase);
}
