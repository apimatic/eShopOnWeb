using System;
using System.Collections.Generic;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
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

    // Additive: lifecycle state so an order can be dispatched or cancelled after checkout.
    // Defaults to Placed for both new and previously-persisted orders.
    public OrderStatus Status { get; private set; } = OrderStatus.Placed;

    public void MarkDispatched()
    {
        if (Status == OrderStatus.Cancelled)
        {
            throw new InvalidOrderStateException($"Order {Id} was cancelled and cannot be dispatched.");
        }

        if (Status == OrderStatus.Dispatched)
        {
            throw new InvalidOrderStateException($"Order {Id} has already been dispatched.");
        }

        Status = OrderStatus.Dispatched;
    }

    // An order may be cancelled after dispatch as well as before: cancelling a dispatched order is
    // exactly when a not-yet-sent delivery follow-up must be called off.
    public void MarkCancelled()
    {
        if (Status == OrderStatus.Cancelled)
        {
            throw new InvalidOrderStateException($"Order {Id} has already been cancelled.");
        }

        Status = OrderStatus.Cancelled;
    }

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
}
