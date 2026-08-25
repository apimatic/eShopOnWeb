using System;
using System.Collections.Generic;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class Order : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private Order() { }
#pragma warning restore CS8618

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
    public OrderPaymentStatus PaymentStatus { get; private set; } = OrderPaymentStatus.Pending;
    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? CaptureId { get; private set; }
    public decimal CapturedAmount { get; private set; }
    public decimal PayPalFee { get; private set; }
    public decimal NetAmount { get; private set; }
    public decimal TotalRefunded { get; private set; }
    public DateTimeOffset? AuthorizationExpiry { get; private set; }
    public DateTimeOffset? AuthorizationCreatedAt { get; private set; }

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

    public void SetPaymentAuthorized(string paypalOrderId, string authorizationId,
        DateTimeOffset? expiry, DateTimeOffset? createdAt)
    {
        PayPalOrderId = paypalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationExpiry = expiry;
        AuthorizationCreatedAt = createdAt;
        PaymentStatus = OrderPaymentStatus.Authorized;
    }

    public void RenewAuthorization(string newAuthorizationId, DateTimeOffset? newExpiry)
    {
        AuthorizationId = newAuthorizationId;
        AuthorizationExpiry = newExpiry;
    }

    public void SetPaymentCaptured(string captureId, decimal capturedAmount, decimal fee, decimal net)
    {
        CaptureId = captureId;
        CapturedAmount = capturedAmount;
        PayPalFee = fee;
        NetAmount = net;
        PaymentStatus = OrderPaymentStatus.Captured;
    }

    public void SetPaymentCancelled()
    {
        PaymentStatus = OrderPaymentStatus.Cancelled;
    }

    public OrderRefund AddRefund(string refundId, decimal amount, string idempotencyKey)
    {
        var refund = new OrderRefund(Id, refundId, amount, idempotencyKey);
        _refunds.Add(refund);
        TotalRefunded += amount;
        PaymentStatus = TotalRefunded >= CapturedAmount
            ? OrderPaymentStatus.Refunded
            : OrderPaymentStatus.PartiallyRefunded;
        return refund;
    }
}
