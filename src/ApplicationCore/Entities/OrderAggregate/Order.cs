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

    /// <summary>
    /// Where this order sits between being placed and the money settling. Orders placed before any
    /// payment is taken start here, which is why the default is <see cref="OrderLifecycleStatus.AwaitingPayment"/>.
    /// </summary>
    public OrderLifecycleStatus Status { get; private set; } = OrderLifecycleStatus.AwaitingPayment;

    public void MarkAuthorized()
    {
        RequireStatus(OrderLifecycleStatus.AwaitingPayment, "authorize payment for");
        Status = OrderLifecycleStatus.Authorized;
    }

    public void MarkFulfilled()
    {
        RequireStatus(OrderLifecycleStatus.Authorized, "fulfil");
        Status = OrderLifecycleStatus.Fulfilled;
    }

    public void MarkCancelled()
    {
        if (Status is not (OrderLifecycleStatus.AwaitingPayment or OrderLifecycleStatus.Authorized))
        {
            throw new OrderStateException(
                $"Order {Id} cannot be cancelled because it is {Status}. Only an order awaiting payment " +
                "or holding an authorization can be cancelled; a fulfilled order must be refunded instead.");
        }

        Status = OrderLifecycleStatus.Cancelled;
    }

    public void MarkRefunded(bool fully)
    {
        if (Status is not (OrderLifecycleStatus.Fulfilled or OrderLifecycleStatus.PartiallyRefunded))
        {
            throw new OrderStateException(
                $"Order {Id} cannot be refunded because it is {Status}. Only a fulfilled order has a " +
                "captured payment to refund.");
        }

        Status = fully ? OrderLifecycleStatus.Refunded : OrderLifecycleStatus.PartiallyRefunded;
    }

    private void RequireStatus(OrderLifecycleStatus expected, string action)
    {
        if (Status != expected)
        {
            throw new OrderStateException(
                $"Order {Id} cannot {action} because it is {Status}; it must be {expected}.");
        }
    }
}
