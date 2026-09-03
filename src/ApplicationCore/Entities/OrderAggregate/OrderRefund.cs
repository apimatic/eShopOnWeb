using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderRefund : BaseEntity
{
#pragma warning disable CS8618
    private OrderRefund() { }
#pragma warning restore CS8618

    public OrderRefund(string idempotencyKey, string providerRefundId, decimal amount,
        string status, DateTimeOffset createdAt)
    {
        IdempotencyKey = idempotencyKey;
        ProviderRefundId = providerRefundId;
        Amount = amount;
        Status = status;
        CreatedAt = createdAt;
    }

    public string IdempotencyKey { get; private set; }
    public string ProviderRefundId { get; private set; }
    public decimal Amount { get; private set; }
    public string Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    internal void UpdateStatus(string status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            throw new ArgumentException("Refund status is required.", nameof(status));
        }
        Status = status;
    }
}
