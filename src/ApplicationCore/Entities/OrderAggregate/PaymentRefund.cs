using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class PaymentRefund : BaseEntity
{
    private PaymentRefund() { }

    public PaymentRefund(string idempotencyKey, string providerRequestId, decimal amount, string currency)
    {
        IdempotencyKey = idempotencyKey;
        ProviderRequestId = providerRequestId;
        Amount = amount;
        Currency = currency;
        Status = "RESERVED";
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string ProviderRequestId { get; private set; } = string.Empty;
    public string? PayPalRefundId { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    public bool ReservesFunds => Status is "RESERVED" or "PENDING" or "COMPLETED";
    public bool IsCompleted => Status == "COMPLETED";

    public void RecordProviderResult(string refundId, string status, decimal amount, DateTimeOffset? updatedAt)
    {
        PayPalRefundId = refundId;
        Status = status;
        Amount = amount;
        UpdatedAt = updatedAt ?? DateTimeOffset.UtcNow;
    }

    public void Release(string status)
    {
        Status = status;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
