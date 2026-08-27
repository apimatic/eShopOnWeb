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

    public OrderStatus Status { get; private set; } = OrderStatus.PendingPayment;

    /// <summary>
    /// Payment state for this order. Null until the order has been paid (authorized).
    /// Owned by the order; carries the PayPal-owned identifiers and statuses.
    /// </summary>
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

    public void MarkAuthorized(OrderPayment payment)
    {
        Guard.Against.Null(payment, nameof(payment));
        if (Status != OrderStatus.PendingPayment)
        {
            throw new Exceptions.PaymentConflictException($"Order {Id} is not awaiting payment (status: {Status}).");
        }
        Payment = payment;
        Status = OrderStatus.AwaitingFulfilment;
    }

    public void MarkFulfilled()
    {
        if (Status == OrderStatus.Fulfilled)
        {
            return; // idempotent
        }
        if (Status != OrderStatus.AwaitingFulfilment)
        {
            throw new Exceptions.PaymentConflictException($"Order {Id} cannot be fulfilled from status {Status}.");
        }
        Status = OrderStatus.Fulfilled;
    }

    public void Cancel()
    {
        if (Status == OrderStatus.Cancelled)
        {
            return; // idempotent
        }
        if (Status == OrderStatus.Fulfilled)
        {
            throw new Exceptions.PaymentConflictException(
                $"Order {Id} has already been fulfilled and its payment captured; issue a refund instead of cancelling.");
        }
        Status = OrderStatus.Cancelled;
    }
}
