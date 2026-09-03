using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class PaymentRefund : BaseEntity
{
    private PaymentRefund() { }

    internal PaymentRefund(string idempotencyKey, string providerRequestId, decimal amount)
    {
        IdempotencyKey = idempotencyKey;
        ProviderRequestId = providerRequestId;
        Amount = amount;
        Status = "PENDING";
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public int OrderPaymentId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string ProviderRequestId { get; private set; } = string.Empty;
    public string? ProviderRefundId { get; private set; }
    public decimal Amount { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public bool ReservesFunds => Status is not "FAILED" and not "CANCELLED";

    internal void RecordProviderResult(string providerRefundId, string status, decimal amount)
    {
        ProviderRefundId = providerRefundId;
        Status = status;
        Amount = amount;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    internal void MarkFailed()
    {
        Status = "FAILED";
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
