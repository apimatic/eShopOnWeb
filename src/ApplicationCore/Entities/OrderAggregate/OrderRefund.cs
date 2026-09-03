using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderRefund : BaseEntity
{
    private OrderRefund() { }

    internal OrderRefund(string idempotencyKey, decimal amount)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        IdempotencyKey = idempotencyKey;
        Amount = amount;
    }

    public string IdempotencyKey { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public string? PayPalRefundId { get; private set; }
    public string? PayPalStatus { get; private set; }
    public PaymentOperationStatus Status { get; private set; } = PaymentOperationStatus.Pending;
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; private set; }

    public void Complete(string refundId, string status)
    {
        Guard.Against.NullOrEmpty(refundId, nameof(refundId));
        PayPalRefundId = refundId;
        PayPalStatus = status;
        Status = PaymentOperationStatus.Completed;
        CompletedAt = DateTimeOffset.UtcNow;
    }

    public void Fail(string? providerStatus)
    {
        PayPalStatus = providerStatus;
        Status = PaymentOperationStatus.Failed;
    }

    public void MarkPending(string refundId, string status)
    {
        Guard.Against.NullOrEmpty(refundId, nameof(refundId));
        PayPalRefundId = refundId;
        PayPalStatus = status;
        Status = PaymentOperationStatus.Pending;
    }

    public void MarkUnknown() => Status = PaymentOperationStatus.Unknown;
}
