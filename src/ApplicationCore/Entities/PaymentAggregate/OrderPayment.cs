using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

public class OrderPayment : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618
    private OrderPayment() { }

    public OrderPayment(int orderId)
    {
        OrderId = orderId;
        Status = OrderPaymentStatus.PendingPayment;
    }

    public int OrderId { get; private set; }
    public OrderPaymentStatus Status { get; private set; }

    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? CaptureId { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public decimal TotalRefunded { get; private set; }

    private readonly List<OrderRefund> _refunds = new();
    public IReadOnlyCollection<OrderRefund> Refunds => _refunds.AsReadOnly();

    public void RecordAuthorization(string payPalOrderId, string authorizationId)
    {
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        Status = OrderPaymentStatus.Authorized;
    }

    public void UpdateAuthorizationId(string newAuthorizationId)
    {
        AuthorizationId = newAuthorizationId;
    }

    public void RecordCapture(string captureId, decimal capturedAmount, decimal? payPalFee, decimal? netAmount)
    {
        CaptureId = captureId;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        Status = OrderPaymentStatus.Captured;
    }

    public void RecordVoid()
    {
        Status = OrderPaymentStatus.Voided;
    }

    public void AddRefund(string idempotencyKey, string? payPalRefundId, decimal amount)
    {
        var refund = new OrderRefund(Id, idempotencyKey, payPalRefundId, amount);
        _refunds.Add(refund);
        TotalRefunded += amount;
        Status = TotalRefunded >= CapturedAmount
            ? OrderPaymentStatus.Refunded
            : OrderPaymentStatus.PartiallyRefunded;
    }

    public bool TryGetExistingRefund(string idempotencyKey, out OrderRefund? existing)
    {
        foreach (var r in _refunds)
        {
            if (r.IdempotencyKey == idempotencyKey)
            {
                existing = r;
                return true;
            }
        }
        existing = null;
        return false;
    }

    public decimal RemainingRefundable => (CapturedAmount ?? 0m) - TotalRefunded;
}
