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
        PaymentReference = Guid.NewGuid().ToString("N");
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;
    public string PaymentReference { get; private set; }
    public OrderPayment? Payment { get; private set; }

    // DDD Patterns comment
    // Using a private collection field, better for DDD Aggregate's encapsulation
    // so OrderItems cannot be added from "outside the AggregateRoot" directly to the collection,
    // but only through the method Order.AddOrderItem() which includes behavior.
    private readonly List<OrderItem> _orderItems = new List<OrderItem>();

    // Using List<>.AsReadOnly() 
    // This will create a read only wrapper around the private list so is protected against "external updates".
    // It's much cheaper than .ToList() because it will not have to copy all items in a new collection. (Just one heap alloc for the wrapper instance)
    //https://msdn.microsoft.com/en-us/library/e78dcd75(v=vs.110).aspx 
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

    public void SetPayPalOrder(string payPalOrderId)
    {
        Payment ??= new OrderPayment(Total());
        Payment.SetPayPalOrder(payPalOrderId);
    }

    public void RecordAuthorization(string authorizationId, string status, decimal amount,
        DateTimeOffset? createdAt, DateTimeOffset? expiresAt)
    {
        if (amount != Total())
        {
            throw new InvalidOperationException("The authorized amount does not equal the order total.");
        }

        Payment ??= new OrderPayment(Total());
        Payment.RecordAuthorization(authorizationId, status, amount, createdAt, expiresAt);
        Status = OrderStatus.Authorized;
    }

    public void RecordCapture(string captureId, string status, decimal amount, decimal? fee,
        decimal? net, DateTimeOffset? capturedAt)
    {
        if (Payment is null)
        {
            throw new InvalidOperationException("The order has no payment to capture.");
        }

        Payment.RecordCapture(captureId, status, amount, fee, net, capturedAt);
        if (string.Equals(status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
        {
            Status = OrderStatus.Fulfilled;
        }
    }

    public void MarkCancelled(string authorizationStatus)
    {
        if (Payment is null)
        {
            throw new InvalidOperationException("The order has no payment authorization to cancel.");
        }

        Payment.SetAuthorizationStatus(authorizationStatus);
        Status = OrderStatus.Cancelled;
    }

    public OrderRefund AddRefund(string idempotencyKey, string payPalRefundId, string status,
        decimal amount, DateTimeOffset? createdAt)
    {
        if (Payment is null || Payment.CapturedAmount is null)
        {
            throw new InvalidOperationException("The order has no captured payment to refund.");
        }

        var refund = Payment.AddRefund(idempotencyKey, payPalRefundId, status, amount, createdAt);
        RefreshRefundStatus();
        return refund;
    }

    public void UpdateRefund(string idempotencyKey, string status, decimal amount)
    {
        if (Payment is null)
        {
            throw new InvalidOperationException("The order has no payment.");
        }

        Payment.UpdateRefund(idempotencyKey, status, amount);
        RefreshRefundStatus();
    }

    private void RefreshRefundStatus()
    {
        if (Payment?.CapturedAmount is not decimal captured)
        {
            return;
        }

        var refunded = Payment.RefundedAmount();
        Status = refunded <= 0
            ? OrderStatus.Fulfilled
            : refunded >= captured
                ? OrderStatus.Refunded
                : OrderStatus.PartiallyRefunded;
    }
}
