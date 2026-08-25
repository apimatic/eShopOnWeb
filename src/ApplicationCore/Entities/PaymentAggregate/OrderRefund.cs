using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class OrderRefund : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private OrderRefund() { }
#pragma warning restore CS8618

    public OrderRefund(int paymentId, string payPalRefundId, string callerIdempotencyKey,
        string refundStatus, string amountValue, string amountCurrency)
    {
        PaymentId = paymentId;
        PayPalRefundId = payPalRefundId;
        CallerIdempotencyKey = callerIdempotencyKey;
        RefundStatus = refundStatus;
        AmountValue = amountValue;
        AmountCurrency = amountCurrency;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public int PaymentId { get; private set; }
    public string PayPalRefundId { get; private set; } = string.Empty;
    public string CallerIdempotencyKey { get; private set; } = string.Empty;
    public string RefundStatus { get; private set; } = string.Empty;
    public string AmountValue { get; private set; } = string.Empty;
    public string AmountCurrency { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
}
