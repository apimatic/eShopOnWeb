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
    /// The fulfilment lifecycle state. A new order awaits payment; it never ends checkout with money
    /// taken. Payment, fulfilment, cancellation and refunds move it through the rest of the lifecycle.
    /// </summary>
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;

    /// <summary>
    /// The money movement for this order (the PayPal hold/capture/refund state). Null until the
    /// shopper pays. Part of the Order aggregate — mutated only through this root.
    /// </summary>
    public Payment? Payment { get; private set; }

    /// <summary>Records that a hold has been placed on the shopper's funds for this order.</summary>
    public void SetAuthorized(Payment payment)
    {
        Guard.Against.Null(payment, nameof(payment));
        if (Status != OrderStatus.AwaitingPayment)
        {
            throw new InvalidOperationException($"Order {Id} cannot be paid from status {Status}.");
        }

        Payment = payment;
        Status = OrderStatus.PaymentAuthorized;
    }

    /// <summary>Marks the order fulfilled — the point at which the held money is taken.</summary>
    public void SetFulfilled()
    {
        if (Status != OrderStatus.PaymentAuthorized)
        {
            throw new InvalidOperationException($"Order {Id} cannot be fulfilled from status {Status}.");
        }

        Status = OrderStatus.Fulfilled;
    }

    /// <summary>Cancels the order before fulfilment, releasing the shopper's held funds.</summary>
    public void Cancel()
    {
        if (Status != OrderStatus.PaymentAuthorized)
        {
            throw new InvalidOperationException($"Order {Id} cannot be cancelled from status {Status}.");
        }

        Status = OrderStatus.Cancelled;
    }

    /// <summary>Marks a previously fulfilled order as fully refunded.</summary>
    public void MarkRefunded()
    {
        if (Status == OrderStatus.Fulfilled)
        {
            Status = OrderStatus.Refunded;
        }
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
