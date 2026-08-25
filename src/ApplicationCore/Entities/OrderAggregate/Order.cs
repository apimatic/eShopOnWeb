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
    public OrderStatus Status { get; private set; } = OrderStatus.PendingPayment;
    public PaymentInfo? Payment { get; private set; }

    private readonly List<OrderItem> _orderItems = new List<OrderItem>();

    public IReadOnlyCollection<OrderItem> OrderItems => _orderItems.AsReadOnly();

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
        string authorizationStatus, string? expirationTime)
    {
        Status = OrderStatus.PaymentAuthorized;
        Payment = new PaymentInfo(paypalOrderId, authorizationId, authorizationStatus, expirationTime);
    }

    public void SetFulfilled(string captureId, decimal capturedAmount, decimal fee, decimal net, string captureStatus)
    {
        Status = OrderStatus.Fulfilled;
        Payment!.RecordCapture(captureId, capturedAmount, fee, net, captureStatus);
    }

    public void SetCancelled()
    {
        Status = OrderStatus.Cancelled;
    }

    public void UpdateAuthorization(string newAuthId, string newStatus, string? newExpiry)
    {
        Payment!.UpdateAuthorization(newAuthId, newStatus, newExpiry);
    }

    public string AddRefund(string idempotencyKey, string refundId, decimal amount, string currency, string status)
    {
        var refund = new OrderRefund
        {
            IdempotencyKey = idempotencyKey,
            RefundId = refundId,
            Amount = amount,
            Currency = currency,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow
        };
        Payment!.AddRefund(refund);

        var totalRefunded = Payment.TotalRefunded();
        Status = totalRefunded >= Payment.CapturedAmount
            ? OrderStatus.Refunded
            : OrderStatus.PartiallyRefunded;

        return refundId;
    }
}
