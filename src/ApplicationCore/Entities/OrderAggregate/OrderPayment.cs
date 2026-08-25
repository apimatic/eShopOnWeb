using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Enums;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderPayment : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private OrderPayment() { }
#pragma warning restore CS8618

    public OrderPayment(int orderId, string paypalOrderId, decimal amount, string currency)
    {
        OrderId = orderId;
        PayPalOrderId = paypalOrderId;
        Amount = amount;
        Currency = currency;
        Status = PaymentStatus.PendingPayment;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string PayPalOrderId { get; private set; }
    public string? PayPalAuthorizationId { get; private set; }
    public string? PayPalCaptureId { get; private set; }
    public PaymentStatus Status { get; private set; }
    public string Currency { get; private set; }
    public decimal Amount { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public void SetAuthorized(string paypalAuthorizationId)
    {
        PayPalAuthorizationId = paypalAuthorizationId;
        Status = PaymentStatus.Authorized;
    }

    public void UpdateAuthorizationId(string newAuthorizationId)
    {
        PayPalAuthorizationId = newAuthorizationId;
    }

    public void SetCaptured(string captureId, decimal capturedAmount, decimal paypalFee, decimal netAmount)
    {
        PayPalCaptureId = captureId;
        CapturedAmount = capturedAmount;
        PayPalFee = paypalFee;
        NetAmount = netAmount;
        Status = PaymentStatus.Captured;
    }

    public void SetVoided()
    {
        Status = PaymentStatus.Voided;
    }

    public PaymentRefund AddRefund(string paypalRefundId, decimal amount, string idempotencyKey)
    {
        var totalRefunded = _refunds.Sum(r => r.Amount) + amount;
        if (totalRefunded > CapturedAmount.GetValueOrDefault())
            throw new InvalidOperationException(
                $"Refund of {amount} would exceed captured amount of {CapturedAmount}.");

        var refund = new PaymentRefund(paypalRefundId, amount, Currency, idempotencyKey);
        _refunds.Add(refund);

        Status = totalRefunded >= CapturedAmount.GetValueOrDefault()
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;

        return refund;
    }

    public decimal RemainingRefundable =>
        CapturedAmount.GetValueOrDefault() - _refunds.Sum(r => r.Amount);
}
