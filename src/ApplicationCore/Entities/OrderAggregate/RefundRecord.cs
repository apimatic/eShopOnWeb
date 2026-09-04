using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class RefundRecord : BaseEntity
{
    private RefundRecord() { }
    public RefundRecord(int orderId, string idempotencyKey, string? providerRefundId, decimal amount, string status)
    {
        OrderId = orderId; IdempotencyKey = idempotencyKey; ProviderRefundId = providerRefundId; Amount = amount; Status = status;
        CreatedAt = DateTimeOffset.UtcNow;
    }
    public int OrderId { get; private set; }
    public string IdempotencyKey { get; private set; } = null!;
    public string? ProviderRefundId { get; private set; }
    public decimal Amount { get; private set; }
    public string Status { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
}
