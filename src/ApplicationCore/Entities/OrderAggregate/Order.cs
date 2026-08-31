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
        PaymentReference = Guid.NewGuid();
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public Guid PaymentReference { get; private set; }
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;
    public DateTimeOffset? FulfilledAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }

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

    public void MarkAuthorized()
    {
        if (Status != OrderStatus.AwaitingPayment)
        {
            throw new InvalidOperationException($"Order {Id} cannot be authorized from {Status}.");
        }

        Status = OrderStatus.Authorized;
    }

    public void MarkFulfilled(DateTimeOffset occurredAt)
    {
        if (Status == OrderStatus.Fulfilled) return;
        if (Status != OrderStatus.Authorized)
        {
            throw new InvalidOperationException($"Order {Id} cannot be fulfilled from {Status}.");
        }

        Status = OrderStatus.Fulfilled;
        FulfilledAt = occurredAt;
    }

    public void MarkCancelled(DateTimeOffset occurredAt)
    {
        if (Status == OrderStatus.Cancelled) return;
        if (Status is not (OrderStatus.AwaitingPayment or OrderStatus.Authorized))
        {
            throw new InvalidOperationException($"Order {Id} cannot be cancelled from {Status}.");
        }

        Status = OrderStatus.Cancelled;
        CancelledAt = occurredAt;
    }

    public void MarkRefunded(bool fullyRefunded)
    {
        if (Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded))
        {
            throw new InvalidOperationException($"Order {Id} cannot be refunded from {Status}.");
        }

        Status = fullyRefunded ? OrderStatus.Refunded : OrderStatus.PartiallyRefunded;
    }
}

public enum OrderStatus
{
    AwaitingPayment,
    Authorized,
    Fulfilled,
    Cancelled,
    PartiallyRefunded,
    Refunded
}
