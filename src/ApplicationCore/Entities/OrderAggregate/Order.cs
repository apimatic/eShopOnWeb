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
    /// The fulfilment lifecycle of the order. Additive to the original model: an order now starts
    /// awaiting payment rather than being implicitly complete on creation.
    /// </summary>
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;

    /// <summary>Records that the order total has been authorized (funds held) with the processor.</summary>
    public void MarkAuthorized()
    {
        if (Status != OrderStatus.AwaitingPayment && Status != OrderStatus.Authorized)
        {
            throw new InvalidOperationException($"Order {Id} cannot be authorized from status {Status}.");
        }
        Status = OrderStatus.Authorized;
    }

    /// <summary>Records that the operator fulfilled the order and the held funds were captured.</summary>
    public void MarkFulfilled()
    {
        if (Status != OrderStatus.Authorized)
        {
            throw new InvalidOperationException($"Order {Id} must be authorized before it can be fulfilled (current status {Status}).");
        }
        Status = OrderStatus.Fulfilled;
    }

    /// <summary>Records that the order was cancelled before fulfilment and any held funds released.</summary>
    public void MarkCancelled()
    {
        if (Status == OrderStatus.Fulfilled)
        {
            throw new InvalidOperationException($"Order {Id} has already been fulfilled and can no longer be cancelled; issue a refund instead.");
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
