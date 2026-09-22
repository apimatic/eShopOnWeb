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

    /// <summary>
    /// How far this order has moved for notification purposes. Defaults to <see cref="OrderNotificationStatus.Placed"/>.
    /// </summary>
    public OrderNotificationStatus NotificationStatus { get; private set; } = OrderNotificationStatus.Placed;

    /// <summary>
    /// Move the order to Dispatched. Returns <c>true</c> only if the state actually changed, so the
    /// caller can gate the "on its way" message and the scheduled follow-up on a real transition and
    /// not re-notify an order that was already dispatched (or cancelled).
    /// </summary>
    public bool TryMarkDispatched()
    {
        if (NotificationStatus != OrderNotificationStatus.Placed)
        {
            return false;
        }

        NotificationStatus = OrderNotificationStatus.Dispatched;
        return true;
    }

    /// <summary>
    /// Move the order to Cancelled. Returns <c>true</c> only if the state actually changed. An order
    /// may be cancelled whether it was Placed or Dispatched, but never twice.
    /// </summary>
    public bool TryMarkCancelled()
    {
        if (NotificationStatus == OrderNotificationStatus.Cancelled)
        {
            return false;
        }

        NotificationStatus = OrderNotificationStatus.Cancelled;
        return true;
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
