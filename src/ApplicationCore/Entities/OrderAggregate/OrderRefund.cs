using System;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderRefund : BaseEntity
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderRefund() { }

    internal OrderRefund(string idempotencyKey, decimal amount, string currency)
    {
        IdempotencyKey = idempotencyKey;
        Amount = amount;
        Currency = currency;
        Status = RefundStatus.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string IdempotencyKey { get; private set; }
    public string? PayPalRefundId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; }
    public RefundStatus Status { get; private set; }
    public string? PayPalStatus { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    internal void Complete(string payPalRefundId, string status, DateTimeOffset completedAt)
    {
        PayPalRefundId = payPalRefundId;
        PayPalStatus = status;
        Status = RefundStatus.Completed;
        FailureReason = null;
        CompletedAt = completedAt;
    }

    internal void RecordPending(string payPalRefundId, string status)
    {
        PayPalRefundId = payPalRefundId;
        PayPalStatus = status;
        Status = RefundStatus.Pending;
    }

    internal void Fail(string? failureReason)
    {
        Status = RefundStatus.Failed;
        FailureReason = failureReason;
    }
}

public enum RefundStatus
{
    Pending,
    Completed,
    Failed
}
