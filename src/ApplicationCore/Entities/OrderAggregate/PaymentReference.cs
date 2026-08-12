using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class PaymentReference : BaseEntity, IAggregateRoot
{
    public int OrderId { get; private set; }
    public PaymentState State { get; private set; }

    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? CaptureId { get; private set; }

    public string? SavedPaymentMethodId { get; private set; }

    public decimal? AuthorizedAmount { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PaypalFee { get; private set; }
    public string? Currency { get; private set; }

    public string? RefundIdempotencyKey { get; private set; }
    public List<string> RefundIds { get; private set; } = new();
    public decimal RefundedAmount { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.Now;

    #pragma warning disable CS8618
    private PaymentReference() {}

    public PaymentReference(int orderId)
    {
        OrderId = orderId;
        State = PaymentState.AwaitingPayment;
    }

    public void SetAuthorization(string payPalOrderId, string authorizationId, decimal authorizedAmount, string currency)
    {
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        AuthorizedAmount = authorizedAmount;
        Currency = currency;
        State = PaymentState.Authorized;
        UpdatedAt = DateTimeOffset.Now;
    }

    public void SetCapture(string captureId, decimal capturedAmount, decimal? fee)
    {
        CaptureId = captureId;
        CapturedAmount = capturedAmount;
        PaypalFee = fee ?? 0m;
        State = PaymentState.Captured;
        UpdatedAt = DateTimeOffset.Now;
    }

    public void SetCancelled()
    {
        State = PaymentState.Cancelled;
        UpdatedAt = DateTimeOffset.Now;
    }

    public void AddRefund(string refundId, decimal refundedAmount)
    {
        RefundIds.Add(refundId);
        RefundedAmount += refundedAmount;
        State = PaymentState.RefundCompleted;
        UpdatedAt = DateTimeOffset.Now;
    }
}
