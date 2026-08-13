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

    /// <summary>
    /// Where the order is in its lifecycle. Additive to the original aggregate so operators can
    /// mark an order dispatched or cancelled and drive the notification flow off the transition.
    /// </summary>
    public OrderStatus Status { get; private set; } = OrderStatus.Placed;

    /// <summary>Marks the order dispatched. Rejects a repeat dispatch or dispatching a cancelled order.</summary>
    public void MarkDispatched()
    {
        if (Status == OrderStatus.Cancelled)
            throw new InvalidOrderStateException($"Order {Id} is cancelled and cannot be dispatched.");
        if (Status == OrderStatus.Dispatched)
            throw new InvalidOrderStateException($"Order {Id} has already been dispatched.");

        Status = OrderStatus.Dispatched;
    }

    /// <summary>Marks the order cancelled. Allowed from Placed or Dispatched; rejects a repeat cancel.</summary>
    public void MarkCancelled()
    {
        if (Status == OrderStatus.Cancelled)
            throw new InvalidOrderStateException($"Order {Id} has already been cancelled.");

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
