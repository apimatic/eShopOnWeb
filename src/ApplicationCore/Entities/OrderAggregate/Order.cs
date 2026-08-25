using System;
using System.Collections.Generic;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class Order : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Order() {}

    public Order(string buyerId, Address shipToAddress, List<OrderItem> items)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        BuyerId = buyerId;
        ShipToAddress = shipToAddress;
        _orderItems = items;
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }

    private readonly List<OrderItem> _orderItems = new List<OrderItem>();
    public IReadOnlyCollection<OrderItem> OrderItems => _orderItems.AsReadOnly();

    // Payment state
    public PaymentStatus PaymentStatus { get; private set; } = PaymentStatus.AwaitingPayment;
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? CaptureId { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    private readonly List<OrderRefund> _refunds = new List<OrderRefund>();
    public IReadOnlyCollection<OrderRefund> Refunds => _refunds.AsReadOnly();

    public decimal Total()
    {
        var total = 0m;
        foreach (var item in _orderItems)
        {
            total += item.UnitPrice * item.Units;
        }
        return total;
    }

    public decimal TotalRefunded()
    {
        var total = 0m;
        foreach (var r in _refunds) total += r.Amount;
        return total;
    }

    public void RecordAuthorization(string payPalOrderId, string authorizationId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        PaymentStatus = PaymentStatus.Authorized;
    }

    public void UpdateAuthorizationId(string newAuthorizationId)
    {
        Guard.Against.NullOrEmpty(newAuthorizationId, nameof(newAuthorizationId));
        AuthorizationId = newAuthorizationId;
    }

    public void RecordCapture(string captureId, decimal capturedAmount, decimal? paypalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));

        CaptureId = captureId;
        CapturedAmount = capturedAmount;
        PayPalFee = paypalFee;
        NetAmount = netAmount;
        PaymentStatus = PaymentStatus.Fulfilled;
    }

    public void RecordVoid()
    {
        PaymentStatus = PaymentStatus.Cancelled;
    }

    public void RecordRefund(string refundId, string idempotencyKey, decimal amount)
    {
        _refunds.Add(new OrderRefund(refundId, idempotencyKey, amount));
        PaymentStatus = TotalRefunded() >= (CapturedAmount ?? 0)
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
    }
}
