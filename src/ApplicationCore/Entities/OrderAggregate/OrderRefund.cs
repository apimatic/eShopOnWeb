using System;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderRefund : BaseEntity
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderRefund() { }
#pragma warning restore CS8618

    internal OrderRefund(string idempotencyKey, decimal amount, string currency)
    {
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        Currency = currency;
        Status = "RESERVED";
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string IdempotencyKey { get; private set; }
    public string? ProviderRefundId { get; private set; }
    public string Status { get; private set; }
    public string? StatusReason { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public bool CountsAgainstCapture => Status is not ("FAILED" or "CANCELLED");

    public void RecordProviderResult(string providerRefundId, string status, string? statusReason)
    {
        ProviderRefundId = providerRefundId;
        Status = status;
        StatusReason = statusReason;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkFailed(string? reason)
    {
        Status = "FAILED";
        StatusReason = reason;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
