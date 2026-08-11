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
    /// Fulfilment lifecycle. New orders start awaiting payment. Kept on the existing
    /// Order aggregate so the payment capability is additive rather than a parallel model.
    /// </summary>
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;

    public void MarkAuthorized()
    {
        if (Status != OrderStatus.AwaitingPayment && Status != OrderStatus.Authorized)
        {
            throw new InvalidOrderStateException($"Cannot authorize an order in state {Status}.");
        }
        Status = OrderStatus.Authorized;
    }

    public void MarkFulfilled()
    {
        if (Status != OrderStatus.Authorized)
        {
            throw new InvalidOrderStateException($"Cannot fulfil an order in state {Status}; it must be authorized first.");
        }
        Status = OrderStatus.Fulfilled;
    }

    public void MarkCancelled()
    {
        if (Status == OrderStatus.Fulfilled)
        {
            throw new InvalidOrderStateException("Cannot cancel an order that has already been fulfilled; issue a refund instead.");
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
