using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class PaymentRefund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private PaymentRefund() { }

    internal PaymentRefund(string idempotencyKey, decimal amount)
    {
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        Status = "PENDING";
        RequestedAt = DateTimeOffset.UtcNow;
    }

    public int OrderPaymentId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public string? PayPalRefundId { get; private set; }
    public decimal Amount { get; private set; }
    public string Status { get; private set; }
    public DateTimeOffset RequestedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public void Complete(string paypalRefundId, string status, DateTimeOffset completedAt)
    {
        PayPalRefundId = paypalRefundId;
        Status = status;
        CompletedAt = completedAt;
    }
}
