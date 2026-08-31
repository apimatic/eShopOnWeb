using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class PaymentRefund : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private PaymentRefund() { }
#pragma warning restore CS8618

    public PaymentRefund(int orderId, string buyerId, string captureId, string idempotencyKey,
        decimal requestedAmount, string currency)
    {
        OrderId = orderId;
        BuyerId = buyerId;
        CaptureId = captureId;
        IdempotencyKey = idempotencyKey;
        RequestedAmount = requestedAmount;
        Currency = currency;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string CaptureId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public decimal RequestedAmount { get; private set; }
    public decimal? RefundedAmount { get; private set; }
    public string Currency { get; private set; }
    public string? PayPalRefundId { get; private set; }
    public string Status { get; private set; } = "IN_FLIGHT";
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; private set; }

    public bool IsCompleted => string.Equals(Status, "COMPLETED", StringComparison.OrdinalIgnoreCase);
    public bool ReservesFunds => Status is "IN_FLIGHT" or "PENDING";

    public void RecordProviderResult(string refundId, string status, decimal? amount)
    {
        PayPalRefundId = refundId;
        Status = status;
        RefundedAmount = amount;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
