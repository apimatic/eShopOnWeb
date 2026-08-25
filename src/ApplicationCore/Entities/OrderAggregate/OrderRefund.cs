using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// A single refund made against an order's capture. Keyed by a caller-supplied idempotency key so
/// a repeated request under the same key can be recognised and answered without refunding twice,
/// while distinct partial refunds (each with their own key) remain legitimate.
/// </summary>
public class OrderRefund : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderRefund() { }
#pragma warning restore CS8618

    public OrderRefund(string refundId, string idempotencyKey, decimal amount, string status, DateTimeOffset createdAt)
    {
        RefundId = refundId;
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        Status = status;
        CreatedAt = createdAt;
    }

    public string RefundId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public decimal Amount { get; private set; }
    public string Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
