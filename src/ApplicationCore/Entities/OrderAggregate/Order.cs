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
        Status = OrderStatus.AwaitingPayment;
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }

    /// <summary>Fulfilment lifecycle of the order (additive to the original eShop model).</summary>
    public OrderStatus Status { get; private set; }

    /// <summary>
    /// The payment for this order. Null until the shopper pays. Owned by the order aggregate so
    /// its PayPal state travels with the order and survives across separate requests.
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

    /// <summary>
    /// Attaches a freshly-created (pending) payment to an order that is still awaiting payment.
    /// Persisting this before contacting PayPal lets a retry reuse the same idempotency keys.
    /// </summary>
    public void StartPayment(Payment payment)
    {
        Guard.Against.Null(payment, nameof(payment));
        if (Status != OrderStatus.AwaitingPayment)
        {
            throw new InvalidOperationException(
                $"Cannot start a payment for an order in state '{Status}'.");
        }
        Payment = payment;
    }

    /// <summary>Marks the order as authorized once PayPal is holding the funds.</summary>
    public void MarkAuthorized()
    {
        if (Status != OrderStatus.AwaitingPayment)
        {
            throw new InvalidOperationException(
                $"Only an order awaiting payment can be authorized; current state is '{Status}'.");
        }
        Status = OrderStatus.PaymentAuthorized;
    }

    /// <summary>Marks the order fulfilled — the point at which the money is actually captured.</summary>
    public void MarkFulfilled()
    {
        if (Status != OrderStatus.PaymentAuthorized)
        {
            throw new InvalidOperationException(
                $"Only an authorized order can be fulfilled; current state is '{Status}'.");
        }
        Status = OrderStatus.Fulfilled;
    }

    /// <summary>Cancels the order before fulfilment, releasing any held funds.</summary>
    public void MarkCancelled()
    {
        if (Status == OrderStatus.Fulfilled)
        {
            throw new InvalidOperationException("A fulfilled order cannot be cancelled; issue a refund instead.");
        }
        if (Status == OrderStatus.Cancelled)
        {
            return;
        }
        Status = OrderStatus.Cancelled;
    }
}
