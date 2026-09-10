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
    /// Additive payment / fulfilment lifecycle state. Defaults to <see cref="OrderStatus.AwaitingPayment"/>
    /// so the existing catalog/basket/order flow is unaffected.
    /// </summary>
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;

    public void SetAuthorized()
    {
        Guard.Against.OutOfRange((int)Status, nameof(Status), (int)OrderStatus.AwaitingPayment, (int)OrderStatus.Authorized);
        Status = OrderStatus.Authorized;
    }

    public void SetFulfilled()
    {
        if (Status != OrderStatus.Authorized)
        {
            throw new InvalidOperationException($"Only an authorized order can be fulfilled. Current status: {Status}.");
        }
        Status = OrderStatus.Fulfilled;
    }

    public void SetCancelled()
    {
        if (Status != OrderStatus.AwaitingPayment && Status != OrderStatus.Authorized)
        {
            throw new InvalidOperationException($"Only an order that has not been fulfilled can be cancelled. Current status: {Status}.");
        }
        Status = OrderStatus.Cancelled;
    }

    /// <summary>
    /// Reflects a refund against a fulfilled order. <paramref name="fullyRefunded"/> distinguishes a
    /// partial return from one that has given back the entire captured amount.
    /// </summary>
    public void SetRefunded(bool fullyRefunded)
    {
        if (Status != OrderStatus.Fulfilled && Status != OrderStatus.PartiallyRefunded)
        {
            throw new InvalidOperationException($"Only a fulfilled order can be refunded. Current status: {Status}.");
        }
        Status = fullyRefunded ? OrderStatus.Refunded : OrderStatus.PartiallyRefunded;
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
