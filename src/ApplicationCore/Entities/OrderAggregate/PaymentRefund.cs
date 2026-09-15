using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class PaymentRefund : BaseEntity
{
    #pragma warning disable CS8618
    private PaymentRefund() { }

    internal PaymentRefund(string idempotencyKey, decimal amount, string currency)
    {
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        Currency = currency;
        ProviderStatus = "RESERVED";
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public string? ProviderRefundId { get; private set; }
    public string ProviderStatus { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public string? ProviderCreateTime { get; private set; }
    public string? ProviderUpdateTime { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public bool ReservesFunds => ProviderStatus is not "FAILED" and not "CANCELLED";

    public void RecordProviderResult(string refundId, string status, decimal amount,
        string currency, string? createTime, string? updateTime)
    {
        ProviderRefundId = refundId;
        ProviderStatus = status;
        Amount = amount;
        Currency = currency;
        ProviderCreateTime = createTime;
        ProviderUpdateTime = updateTime;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkFailed()
    {
        ProviderStatus = "FAILED";
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
