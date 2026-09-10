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
    /// Payment/fulfilment lifecycle state. Additive to the original model: an order
    /// starts awaiting payment and moves through authorization, fulfilment and refunds.
    /// </summary>
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;

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

    public void MarkPaymentAuthorized()
    {
        if (Status != OrderStatus.AwaitingPayment)
        {
            throw new InvalidOrderStateException(Id, Status, nameof(MarkPaymentAuthorized));
        }
        Status = OrderStatus.PaymentAuthorized;
    }

    public void MarkFulfilled()
    {
        if (Status != OrderStatus.PaymentAuthorized)
        {
            throw new InvalidOrderStateException(Id, Status, nameof(MarkFulfilled));
        }
        Status = OrderStatus.Fulfilled;
    }

    public void MarkCancelled()
    {
        if (Status != OrderStatus.AwaitingPayment && Status != OrderStatus.PaymentAuthorized)
        {
            throw new InvalidOrderStateException(Id, Status, nameof(MarkCancelled));
        }
        Status = OrderStatus.Cancelled;
    }

    public void MarkRefunded(bool partial)
    {
        if (Status != OrderStatus.Fulfilled && Status != OrderStatus.PartiallyRefunded)
        {
            throw new InvalidOrderStateException(Id, Status, nameof(MarkRefunded));
        }
        Status = partial ? OrderStatus.PartiallyRefunded : OrderStatus.Refunded;
    }
}
