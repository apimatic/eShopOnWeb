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

    /// <summary>Payment / fulfilment lifecycle state. Starts awaiting payment.</summary>
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;

    /// <summary>The payment record for this order, carrying PayPal-owned state.
    /// Null until the shopper starts paying.</summary>
    public OrderPayment? Payment { get; private set; }

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

    /// <summary>Attaches a fresh payment record (idempotent: reuses the existing one
    /// if the shopper already began paying).</summary>
    public OrderPayment StartPayment(string currency)
    {
        Payment ??= new OrderPayment(Total(), currency);
        return Payment;
    }

    public void MarkAuthorized()
    {
        Status = OrderStatus.PaymentAuthorized;
    }

    public void MarkFulfilled()
    {
        Status = OrderStatus.Fulfilled;
    }

    public void MarkCancelled()
    {
        Status = OrderStatus.Cancelled;
    }

    /// <summary>Recomputes refund-derived status after a refund is recorded.</summary>
    public void ApplyRefundOutcome()
    {
        if (Payment is null) return;
        if (Payment.RemainingRefundable() <= 0m)
        {
            Status = OrderStatus.Refunded;
        }
        else if (Payment.TotalRefunded() > 0m)
        {
            Status = OrderStatus.PartiallyRefunded;
        }
    }
}
