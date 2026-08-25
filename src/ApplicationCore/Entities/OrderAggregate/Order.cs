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
        Status = OrderStatus.AwaitingPayment;
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public OrderStatus Status { get; private set; }

    // PayPal payment state
    public string? PayPalOrderId { get; private set; }
    public string? PayPalAuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public string? PayPalCaptureId { get; private set; }
    public decimal CapturedAmount { get; private set; }
    public decimal PayPalFee { get; private set; }
    public decimal NetAmount { get; private set; }
    public string? PaymentMethodId { get; private set; }
    public decimal TotalRefunded { get; private set; }

    private readonly List<OrderItem> _orderItems = new List<OrderItem>();
    public IReadOnlyCollection<OrderItem> OrderItems => _orderItems.AsReadOnly();

    private List<OrderRefund> _refunds = new List<OrderRefund>();
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

    public void SetPayPalOrderId(string paypalOrderId) => PayPalOrderId = paypalOrderId;

    public void Authorize(string authorizationId, string status, string? paymentMethodId = null)
    {
        Status = OrderStatus.PaymentAuthorized;
        PayPalAuthorizationId = authorizationId;
        AuthorizationStatus = status;
        PaymentMethodId = paymentMethodId;
    }

    public void UpdateAuthorization(string newAuthorizationId, string status)
    {
        PayPalAuthorizationId = newAuthorizationId;
        AuthorizationStatus = status;
    }

    public void Fulfill(string captureId, decimal capturedAmount, decimal paypalFee, decimal netAmount)
    {
        Status = OrderStatus.Fulfilled;
        PayPalCaptureId = captureId;
        CapturedAmount = capturedAmount;
        PayPalFee = paypalFee;
        NetAmount = netAmount;
    }

    public void Cancel() => Status = OrderStatus.Cancelled;

    public void AddRefund(OrderRefund refund)
    {
        _refunds.Add(refund);
        TotalRefunded += refund.Amount;
    }
}
