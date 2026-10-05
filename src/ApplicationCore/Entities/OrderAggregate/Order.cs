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

    /// <summary>
    /// Payment/fulfilment lifecycle of the order. Orders start awaiting payment; the payment
    /// flow (see <see cref="PaymentAggregate.Payment"/>) moves them forward.
    /// </summary>
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;

    public void MarkPaymentAuthorized()
    {
        if (Status != OrderStatus.AwaitingPayment && Status != OrderStatus.PaymentAuthorized)
            throw new InvalidOperationException($"Order {Id} cannot be authorized while {Status}.");
        Status = OrderStatus.PaymentAuthorized;
    }

    /// <summary>The authorization was lost (expired/voided at the provider); the shopper has to pay again.</summary>
    public void ReturnToAwaitingPayment()
    {
        if (Status != OrderStatus.PaymentAuthorized && Status != OrderStatus.AwaitingPayment)
            throw new InvalidOperationException($"Order {Id} cannot return to awaiting payment while {Status}.");
        Status = OrderStatus.AwaitingPayment;
    }

    public void MarkFulfilled()
    {
        if (Status != OrderStatus.PaymentAuthorized && Status != OrderStatus.Fulfilled)
            throw new InvalidOperationException($"Order {Id} cannot be fulfilled while {Status}.");
        Status = OrderStatus.Fulfilled;
    }

    public void MarkCancelled()
    {
        if (Status == OrderStatus.Fulfilled)
            throw new InvalidOperationException($"Order {Id} is fulfilled and cannot be cancelled.");
        Status = OrderStatus.Cancelled;
    }

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
