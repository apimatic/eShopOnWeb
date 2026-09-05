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
    /// Where the order has got to: awaiting payment, paid for (held), fulfilled or cancelled.
    /// </summary>
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;

    public DateTimeOffset? AuthorizedDate { get; private set; }
    public DateTimeOffset? FulfilledDate { get; private set; }
    public DateTimeOffset? CancelledDate { get; private set; }


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
    /// Records that the order total has been put on hold by the payment processor.
    /// </summary>
    public void MarkAuthorized(DateTimeOffset authorizedDate)
    {
        Guard.Against.NotAllowed(Status != OrderStatus.AwaitingPayment,
            $"An order that is {Status} can no longer be authorized.");

        Status = OrderStatus.Authorized;
        AuthorizedDate = authorizedDate;
    }

    /// <summary>
    /// Records that the held money has been taken and the order handed over.
    /// </summary>
    public void MarkFulfilled(DateTimeOffset fulfilledDate)
    {
        Guard.Against.NotAllowed(Status != OrderStatus.Authorized,
            $"Only an order that is awaiting fulfilment can be fulfilled; this order is {Status}.");

        Status = OrderStatus.Fulfilled;
        FulfilledDate = fulfilledDate;
    }

    /// <summary>
    /// Records that the order was called off before fulfilment.
    /// </summary>
    public void MarkCancelled(DateTimeOffset cancelledDate)
    {
        Guard.Against.NotAllowed(Status == OrderStatus.Fulfilled,
            "A fulfilled order cannot be cancelled; refund it instead.");
        Guard.Against.NotAllowed(Status == OrderStatus.Cancelled,
            "This order has already been cancelled.");

        Status = OrderStatus.Cancelled;
        CancelledDate = cancelledDate;
    }
}
