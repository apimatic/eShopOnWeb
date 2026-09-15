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

    // Additive lifecycle state introduced with the SMS notification feature. Existing checkout
    // flows never touch it, so orders default to Placed.
    public OrderStatus Status { get; private set; } = OrderStatus.Placed;

    /// <summary>
    /// Marks the order as dispatched. Only a placed order can be dispatched; dispatching an
    /// already-dispatched or cancelled order is rejected so callers get an accurate outcome.
    /// </summary>
    public void MarkDispatched()
    {
        if (Status != OrderStatus.Placed)
        {
            throw new InvalidOrderStatusTransitionException(Status, OrderStatus.Dispatched);
        }

        Status = OrderStatus.Dispatched;
    }

    /// <summary>
    /// Marks the order as cancelled. An order can be cancelled while it is placed or already
    /// dispatched (a dispatched order can still be recalled). Cancelling twice is rejected.
    /// </summary>
    public void MarkCancelled()
    {
        if (Status == OrderStatus.Cancelled)
        {
            throw new InvalidOrderStatusTransitionException(Status, OrderStatus.Cancelled);
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
