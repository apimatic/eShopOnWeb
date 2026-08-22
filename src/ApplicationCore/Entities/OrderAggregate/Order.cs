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
        Status = OrderFulfillmentStatus.AwaitingPayment;
        Payment = new OrderPayment();
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public OrderFulfillmentStatus Status { get; private set; } = OrderFulfillmentStatus.AwaitingPayment;
    public OrderPayment Payment { get; private set; } = new OrderPayment();

    // DDD Patterns comment
    // Using a private collection field, better for DDD Aggregate's encapsulation
    // so OrderItems cannot be added from "outside the AggregateRoot" directly to the collection,
    // but only through the method Order.AddOrderItem() which includes behavior.
    private readonly List<OrderItem> _orderItems = new List<OrderItem>();
    private readonly List<OrderRefund> _refunds = new List<OrderRefund>();

    // Using List<>.AsReadOnly() 
    // This will create a read only wrapper around the private list so is protected against "external updates".
    // It's much cheaper than .ToList() because it will not have to copy all items in a new collection. (Just one heap alloc for the wrapper instance)
    //https://msdn.microsoft.com/en-us/library/e78dcd75(v=vs.110).aspx 
    public IReadOnlyCollection<OrderItem> OrderItems => _orderItems.AsReadOnly();
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

    public decimal RemainingRefundable()
    {
        var captured = Payment.CapturedAmount ?? 0m;
        var remaining = captured - TotalRefunded();
        return remaining < 0 ? 0 : remaining;
    }

    public OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    public void MarkAuthorized()
    {
        Status = OrderFulfillmentStatus.Authorized;
    }

    public void MarkFulfilled()
    {
        Status = OrderFulfillmentStatus.Fulfilled;
    }

    public void MarkCancelled()
    {
        Status = OrderFulfillmentStatus.Cancelled;
        Payment.RecordVoid();
    }

    public void AddRefund(OrderRefund refund)
    {
        Guard.Against.Null(refund, nameof(refund));
        _refunds.Add(refund);

        var remaining = RemainingRefundable();
        Status = remaining <= 0m
            ? OrderFulfillmentStatus.Refunded
            : OrderFulfillmentStatus.PartiallyRefunded;
    }
}
