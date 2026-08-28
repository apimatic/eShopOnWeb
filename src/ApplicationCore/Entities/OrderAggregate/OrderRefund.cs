using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderRefund : BaseEntity
{
#pragma warning disable CS8618
    private OrderRefund() { }
#pragma warning restore CS8618

    public OrderRefund(string idempotencyKey, string paypalRefundId, decimal amount,
        string status, DateTimeOffset createdAt)
    {
        IdempotencyKey = Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        PayPalRefundId = Guard.Against.NullOrEmpty(paypalRefundId, nameof(paypalRefundId));
        Amount = amount;
        Status = Guard.Against.NullOrEmpty(status, nameof(status));
        CreatedAt = createdAt;
    }

    public int OrderId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public string PayPalRefundId { get; private set; }
    public decimal Amount { get; private set; }
    public string Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
