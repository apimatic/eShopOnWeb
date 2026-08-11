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

    /// <summary>The fulfilment lifecycle of the order. Starts awaiting payment.</summary>
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;

    /// <summary>
    /// The money state PayPal owns for this order, once a payment hold has been placed.
    /// Null while the order is still awaiting payment.
    /// </summary>
    public Payment? Payment { get; private set; }

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
    /// Attaches the payment hold placed at checkout and moves the order to
    /// <see cref="OrderStatus.PaymentAuthorized"/>.
    /// </summary>
    public void AttachPayment(Payment payment)
    {
        Guard.Against.Null(payment, nameof(payment));
        if (Status != OrderStatus.AwaitingPayment)
        {
            throw new InvalidOperationException($"Order {Id} cannot be paid because it is {Status}.");
        }

        Payment = payment;
        Status = OrderStatus.PaymentAuthorized;
    }

    /// <summary>Marks the order fulfilled; the caller is responsible for having captured the funds.</summary>
    public void MarkFulfilled()
    {
        if (Status != OrderStatus.PaymentAuthorized)
        {
            throw new InvalidOperationException($"Order {Id} cannot be fulfilled because it is {Status}.");
        }

        Status = OrderStatus.Fulfilled;
    }

    /// <summary>Marks the order cancelled; only valid before fulfilment.</summary>
    public void MarkCancelled()
    {
        if (Status != OrderStatus.PaymentAuthorized)
        {
            throw new InvalidOperationException($"Order {Id} cannot be cancelled because it is {Status}.");
        }

        Status = OrderStatus.Cancelled;
    }
}
