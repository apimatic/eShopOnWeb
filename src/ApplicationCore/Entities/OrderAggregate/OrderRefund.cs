using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderRefund : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderRefund() { }
#pragma warning restore CS8618

    public OrderRefund(string payPalRefundId, string idempotencyKey, string status, decimal amount)
    {
        PayPalRefundId = payPalRefundId;
        IdempotencyKey = idempotencyKey;
        Status = status;
        Amount = amount;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string PayPalRefundId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public string Status { get; private set; }
    public decimal Amount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public bool IsCompleted =>
        string.Equals(Status, "COMPLETED", StringComparison.OrdinalIgnoreCase);

    public void UpdateStatus(string status)
    {
        Status = status;
    }
}
