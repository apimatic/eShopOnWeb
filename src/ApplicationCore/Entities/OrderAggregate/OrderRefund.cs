using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single refund taken against an order's captured payment. Part of the <see cref="Order"/> aggregate.
/// The <see cref="IdempotencyKey"/> is the caller-supplied key that makes a repeated refund request a no-op,
/// while two distinct keys against the same capture remain two legitimate partial refunds.
/// </summary>
public class OrderRefund : BaseEntity
{
    public string PayPalRefundId { get; private set; }
    public decimal Amount { get; private set; }
    public string Status { get; private set; }
    public string IdempotencyKey { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderRefund() { }

    public OrderRefund(string payPalRefundId, decimal amount, string status, string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(status, nameof(status));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        PayPalRefundId = payPalRefundId;
        Amount = amount;
        Status = status;
        IdempotencyKey = idempotencyKey;
    }

    public void UpdateStatus(string status)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        Status = status;
    }

    /// <summary>A refund counts against the captured total unless PayPal has explicitly failed/cancelled it.</summary>
    public bool CountsAgainstCapture =>
        !string.Equals(Status, "FAILED", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(Status, "CANCELLED", StringComparison.OrdinalIgnoreCase);
}
