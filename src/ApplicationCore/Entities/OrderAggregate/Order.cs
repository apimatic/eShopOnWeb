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
        PaymentIntentId = Guid.NewGuid().ToString("N");
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }

    /// <summary>
    /// A stable, globally-unique id assigned when the order is placed. Used to derive PayPal
    /// invoice ids and idempotency keys so they never collide across app runs (the in-memory
    /// store resets integer order ids each run) while staying stable for retries of one order.
    /// </summary>
    public string PaymentIntentId { get; private set; }

    /// <summary>Payment lifecycle state. New orders start awaiting payment.</summary>
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;

    /// <summary>The PayPal payment for this order, created when the order is paid (authorized). Null until then.</summary>
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

    /// <summary>Attaches the payment and moves the order to Authorized (funds held, not taken).</summary>
    public void MarkAuthorized(OrderPayment payment)
    {
        Guard.Against.Null(payment, nameof(payment));
        Payment = payment;
        Status = OrderStatus.Authorized;
    }

    /// <summary>Marks the order fulfilled once the authorization has been captured.</summary>
    public void MarkPaid()
    {
        Status = OrderStatus.Paid;
    }

    /// <summary>Marks the order cancelled after its held funds have been released.</summary>
    public void MarkCancelled()
    {
        Status = OrderStatus.Cancelled;
    }
}
