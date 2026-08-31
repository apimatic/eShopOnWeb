using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class PaymentRefund : BaseEntity
{
    private PaymentRefund() { }

    internal PaymentRefund(string idempotencyKey, string providerRefundId, string status, decimal amount)
    {
        IdempotencyKey = idempotencyKey;
        ProviderRefundId = providerRefundId;
        Status = status;
        Amount = amount;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string ProviderRefundId { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void Refresh(string status, decimal amount)
    {
        Status = status;
        Amount = amount;
    }
}
