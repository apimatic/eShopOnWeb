using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderRefund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderRefund() { }

    public OrderRefund(string idempotencyKey, string providerRequestId, decimal amount)
    {
        IdempotencyKey = idempotencyKey;
        ProviderRequestId = providerRequestId;
        Amount = amount;
    }

    public int OrderId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public string ProviderRequestId { get; private set; }
    public string? PayPalRefundId { get; private set; }
    public string Status { get; private set; } = "CREATING";
    public decimal Amount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public void RecordProviderResult(string refundId, string status, decimal amount)
    {
        PayPalRefundId = refundId;
        Status = status;
        Amount = amount;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
