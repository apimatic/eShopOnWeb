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
    /// Fulfilment/processing state of the order. New orders start in
    /// <see cref="OrderStatus.AwaitingPayment"/>.
    /// </summary>
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;

    /// <summary>
    /// Payment state owned by the order (PayPal hold/capture/refunds). Null until the
    /// shopper pays for the order.
    /// </summary>
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

    /// <summary>Attaches the payment (authorization) PayPal just returned for this order.</summary>
    public void Authorize(Payment payment)
    {
        Guard.Against.Null(payment, nameof(payment));

        if (Status == OrderStatus.Fulfilled || Status == OrderStatus.Cancelled)
        {
            throw new Exceptions.InvalidOrderStateException($"Cannot authorize an order that is {Status}.");
        }

        Payment = payment;
        Status = OrderStatus.Authorized;
    }

    /// <summary>Operator flow: money captured at fulfilment.</summary>
    public void MarkFulfilled()
    {
        if (Status != OrderStatus.Authorized)
        {
            throw new Exceptions.InvalidOrderStateException($"Only an authorized order can be fulfilled (current state: {Status}).");
        }
        if (Payment is null ||
            (Payment.Status != PaymentStatus.Captured &&
             Payment.Status != PaymentStatus.PartiallyRefunded &&
             Payment.Status != PaymentStatus.Refunded))
        {
            throw new Exceptions.InvalidOrderStateException("The order cannot be fulfilled until its payment has been captured.");
        }

        Status = OrderStatus.Fulfilled;
    }

    /// <summary>Operator flow: cancel before fulfilment, releasing the hold.</summary>
    public void MarkCancelled()
    {
        if (Status != OrderStatus.AwaitingPayment && Status != OrderStatus.Authorized)
        {
            throw new Exceptions.InvalidOrderStateException($"An order in state {Status} cannot be cancelled; refund the captured payment instead.");
        }

        Status = OrderStatus.Cancelled;
    }
}
