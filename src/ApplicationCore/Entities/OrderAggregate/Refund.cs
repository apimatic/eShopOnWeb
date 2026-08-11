using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single refund against a captured <see cref="Payment"/>. A capture may be refunded more
/// than once (distinct partial refunds), so refunds are a collection on the payment.
/// The <see cref="IdempotencyKey"/> is caller-supplied and makes a repeated refund request a no-op.
/// </summary>
public class Refund : BaseEntity
{
    public string PayPalRefundId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public decimal Amount { get; private set; }
    public string Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private Refund() { }
#pragma warning restore CS8618

    public Refund(string payPalRefundId, string idempotencyKey, decimal amount, string status)
    {
        PayPalRefundId = payPalRefundId;
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        Status = status;
    }

    /// <summary>PayPal reports COMPLETED (or PENDING) as a successful, money-moving refund.</summary>
    public bool CountsAgainstCapture =>
        !string.Equals(Status, "FAILED", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(Status, "CANCELLED", StringComparison.OrdinalIgnoreCase);
}
