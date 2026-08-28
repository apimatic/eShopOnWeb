using System;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class PaymentRefund : BaseEntity
{
    private PaymentRefund() { }

    internal PaymentRefund(string idempotencyKey, string payPalRequestId, decimal amount)
    {
        IdempotencyKey = idempotencyKey;
        PayPalRequestId = payPalRequestId;
        Amount = amount;
        Status = "CREATING";
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public int PaymentId { get; private set; }
    public string IdempotencyKey { get; private set; } = null!;
    public string PayPalRequestId { get; private set; } = null!;
    public string? PayPalRefundId { get; private set; }
    public string Status { get; private set; } = null!;
    public decimal Amount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void MarkProcessed(string payPalRefundId, string status, decimal amount)
    {
        PayPalRefundId = payPalRefundId;
        Status = status;
        Amount = amount;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkFailed()
    {
        Status = "FAILED";
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
