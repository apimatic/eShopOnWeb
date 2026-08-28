using System;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class PaymentRefund : BaseEntity
{
    private PaymentRefund() { }

    public PaymentRefund(string idempotencyKey, decimal amount, string currency)
    {
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        Currency = currency;
        Status = PaymentRefundStatus.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string IdempotencyKey { get; private set; } = string.Empty;
    public string? PayPalRefundId { get; private set; }
    public string? PayPalStatus { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public PaymentRefundStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    public void Complete(string id, string? providerStatus, decimal providerAmount)
    {
        PayPalRefundId = id;
        PayPalStatus = providerStatus;
        Amount = providerAmount;
        Status = string.Equals(providerStatus, "COMPLETED", StringComparison.OrdinalIgnoreCase)
            ? PaymentRefundStatus.Completed : PaymentRefundStatus.Pending;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Fail(string? providerStatus)
    {
        PayPalStatus = providerStatus;
        Status = PaymentRefundStatus.Failed;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

public enum PaymentRefundStatus
{
    Pending,
    Completed,
    Failed,
    Cancelled
}
