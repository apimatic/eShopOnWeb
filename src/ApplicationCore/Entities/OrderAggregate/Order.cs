using System;
using System.Collections.Generic;
using System.Linq;
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
        Status = OrderStatus.PendingPayment;
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public OrderStatus Status { get; private set; } = OrderStatus.PendingPayment;

    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? CaptureId { get; private set; }
    public decimal CapturedAmount { get; private set; }
    public decimal PayPalFee { get; private set; }
    public decimal NetAmount { get; private set; }

    private readonly List<OrderItem> _orderItems = new List<OrderItem>();
    public IReadOnlyCollection<OrderItem> OrderItems => _orderItems.AsReadOnly();

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

    public decimal TotalRefunded() => _refunds.Sum(r => r.Amount);

    public void SetPaymentAuthorized(string payPalOrderId, string authorizationId)
    {
        PayPalOrderId = payPalOrderId;
        AuthorizationId = authorizationId;
        Status = OrderStatus.PaymentAuthorized;
    }

    public void RenewAuthorization(string newAuthorizationId)
    {
        AuthorizationId = newAuthorizationId;
    }

    public void SetCaptured(string captureId, decimal capturedAmount, decimal payPalFee, decimal netAmount)
    {
        CaptureId = captureId;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        Status = OrderStatus.Fulfilled;
    }

    public void SetVoided()
    {
        Status = OrderStatus.Cancelled;
    }

    public OrderRefund AddRefund(string refundId, string idempotencyKey, decimal amount)
    {
        var refund = new OrderRefund(Id, refundId, idempotencyKey, amount);
        _refunds.Add(refund);
        var totalRefunded = TotalRefunded();
        Status = totalRefunded >= CapturedAmount ? OrderStatus.FullyRefunded : OrderStatus.PartiallyRefunded;
        return refund;
    }
}
