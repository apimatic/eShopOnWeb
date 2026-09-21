using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single refund applied against the order's captured payment. Part of the Order aggregate,
/// owned by <see cref="OrderPayment"/>. Carries the PayPal refund id and current status so a later
/// request can act on it, plus the caller-supplied idempotency key used to create it (so a repeat
/// under the same key returns this same refund instead of refunding twice).
/// </summary>
public class OrderRefund
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderRefund() { }
#pragma warning restore CS8618

    public OrderRefund(string payPalRefundId, decimal amount, string status, string idempotencyKey)
    {
        PayPalRefundId = payPalRefundId;
        Amount = amount;
        Status = status;
        IdempotencyKey = idempotencyKey;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>PayPal-generated refund id.</summary>
    public string PayPalRefundId { get; private set; }

    /// <summary>Refunded amount in the order currency.</summary>
    public decimal Amount { get; private set; }

    /// <summary>PayPal refund status (e.g. COMPLETED, PENDING).</summary>
    public string Status { get; private set; }

    /// <summary>Caller-supplied idempotency key that produced this refund.</summary>
    public string IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public void UpdateStatus(string status) => Status = status;
}
