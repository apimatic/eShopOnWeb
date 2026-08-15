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

    /// <summary>Lifecycle of the order with respect to payment. Starts awaiting payment.</summary>
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;

    /// <summary>The PayPal-owned payment state, once the order has been paid (authorized).</summary>
    public Payment? Payment { get; private set; }

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

    // --- Payment lifecycle (additive; the classic checkout flow never calls these) ---

    /// <summary>Attach the hold placed at pay time and move the order to Authorized.</summary>
    public void MarkAuthorized(Payment payment)
    {
        Guard.Against.Null(payment, nameof(payment));
        Payment = payment;
        Status = OrderStatus.Authorized;
    }

    /// <summary>The order has been captured at fulfilment.</summary>
    public void MarkFulfilled() => Status = OrderStatus.Fulfilled;

    /// <summary>The hold was released before fulfilment.</summary>
    public void MarkCancelled() => Status = OrderStatus.Cancelled;

    /// <summary>Reflect a refund against the captured payment.</summary>
    public void MarkRefunded(bool fullyRefunded) =>
        Status = fullyRefunded ? OrderStatus.Refunded : OrderStatus.PartiallyRefunded;
}
