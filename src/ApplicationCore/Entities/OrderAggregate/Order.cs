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

    // ---- Payment / fulfilment state (additive to the original order model) ----

    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;

    /// <summary>The money movement for this order (the hold, the capture, the refunds).</summary>
    public Payment? Payment { get; private set; }

    /// <summary>Creates the payment record (order total to authorize) if it does not exist yet.</summary>
    public Payment InitializePayment(string currencyCode)
    {
        Payment ??= new Payment(Total(), currencyCode);
        return Payment;
    }

    public bool IsAwaitingPayment => Status == OrderStatus.AwaitingPayment;
    public bool IsAuthorized => Status == OrderStatus.PaymentAuthorized;
    public bool IsFulfilled => Status == OrderStatus.Fulfilled;
    public bool IsCancelled => Status == OrderStatus.Cancelled;

    public void MarkAuthorized()
    {
        if (Status != OrderStatus.AwaitingPayment)
        {
            throw new InvalidOperationException($"Cannot authorize an order in status {Status}.");
        }
        Status = OrderStatus.PaymentAuthorized;
    }

    public void MarkFulfilled()
    {
        if (Status != OrderStatus.PaymentAuthorized)
        {
            throw new InvalidOperationException($"Cannot fulfil an order in status {Status}.");
        }
        Status = OrderStatus.Fulfilled;
    }

    public void MarkCancelled()
    {
        if (Status is OrderStatus.Fulfilled or OrderStatus.Cancelled)
        {
            throw new InvalidOperationException($"Cannot cancel an order in status {Status}.");
        }
        Status = OrderStatus.Cancelled;
    }
}
