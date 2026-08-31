using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class PaymentRefund : BaseEntity
{
    private PaymentRefund() { }

    internal PaymentRefund(string idempotencyKey, decimal amount)
    {
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        Status = "CREATING";
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string? PayPalRefundId { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    public void RecordProviderResult(string providerId, string status, decimal amount, DateTimeOffset? updatedAt)
    {
        PayPalRefundId = providerId;
        Status = status;
        Amount = amount;
        UpdatedAt = updatedAt ?? DateTimeOffset.UtcNow;
    }
}
