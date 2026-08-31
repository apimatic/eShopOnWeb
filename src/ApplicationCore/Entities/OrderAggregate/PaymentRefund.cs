using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class PaymentRefund : BaseEntity
{
    private PaymentRefund() { }

    public PaymentRefund(string paypalRefundId, string idempotencyKey, decimal amount,
        string currency, string status, DateTimeOffset createdAt)
    {
        PayPalRefundId = paypalRefundId;
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        Currency = currency;
        Status = status;
        CreatedAt = createdAt;
    }

    public string PayPalRefundId { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    public bool ReservesCapturedFunds => Status is not "FAILED" and not "CANCELLED";

    public void UpdateStatus(string status, DateTimeOffset updatedAt)
    {
        Status = status;
        UpdatedAt = updatedAt;
    }
}
