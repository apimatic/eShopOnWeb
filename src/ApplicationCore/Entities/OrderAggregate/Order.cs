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
    public DateTimeOffset? FulfilledAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
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

    public OrderPayment StartPayment(string currency)
    {
        if (Status != OrderStatus.AwaitingPayment)
            throw new InvalidOperationException("This order is not awaiting payment.");

        Payment ??= new OrderPayment(Id, Total(), currency);
        return Payment;
    }

    public void MarkAuthorized()
    {
        if (Status == OrderStatus.AwaitingPayment)
            Status = OrderStatus.Authorized;
    }

    public void MarkFulfilled(DateTimeOffset fulfilledAt)
    {
        if (Status == OrderStatus.Fulfilled)
            return;
        if (Status != OrderStatus.Authorized)
            throw new InvalidOperationException("Only an authorized order can be fulfilled.");

        Status = OrderStatus.Fulfilled;
        FulfilledAt = fulfilledAt;
    }

    public void MarkCancelled(DateTimeOffset cancelledAt)
    {
        if (Status == OrderStatus.Cancelled)
            return;
        if (Status == OrderStatus.Fulfilled)
            throw new InvalidOperationException("A fulfilled order cannot be cancelled; refund it instead.");

        Status = OrderStatus.Cancelled;
        CancelledAt = cancelledAt;
    }
}

public enum OrderStatus
{
    AwaitingPayment,
    Authorized,
    Fulfilled,
    Cancelled
}
