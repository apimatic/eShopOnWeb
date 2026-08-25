using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderPayment : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618
    private OrderPayment() { }

    public OrderPayment(int orderId, string currency)
    {
        OrderId = orderId;
        Currency = currency;
        PaymentStatus = PaymentStatuses.Pending;
    }

    public int OrderId { get; private set; }
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? CaptureId { get; private set; }
    public List<string> RefundIds { get; private set; } = new();
    public string PaymentStatus { get; private set; } = PaymentStatuses.Pending;
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFeeAmount { get; private set; }
    public decimal? NetAmount { get; private set; }
    public decimal? TotalRefundedAmount { get; private set; }
    public string Currency { get; private set; } = "USD";

    public void SetAuthorized(string payPalOrderId, string authorizationId, DateTimeOffset? expiresAt)
    {
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationExpiresAt = expiresAt;
        PaymentStatus = PaymentStatuses.Authorized;
    }

    public void SetReauthorized(string newAuthorizationId, DateTimeOffset? newExpiresAt)
    {
        AuthorizationId = newAuthorizationId;
        AuthorizationExpiresAt = newExpiresAt;
    }

    public void SetCaptured(string captureId, decimal capturedAmount, decimal? payPalFee, decimal? netAmount)
    {
        CaptureId = captureId;
        CapturedAmount = capturedAmount;
        PayPalFeeAmount = payPalFee;
        NetAmount = netAmount;
        TotalRefundedAmount = 0m;
        PaymentStatus = PaymentStatuses.Captured;
    }

    public void SetVoided()
    {
        PaymentStatus = PaymentStatuses.Voided;
    }

    public void AddRefund(string refundId, decimal refundAmount)
    {
        RefundIds.Add(refundId);
        TotalRefundedAmount = (TotalRefundedAmount ?? 0m) + refundAmount;
        PaymentStatus = TotalRefundedAmount >= CapturedAmount
            ? PaymentStatuses.RefundedFull
            : PaymentStatuses.RefundedPartial;
    }
}
