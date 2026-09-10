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

    /// <summary>Payment/fulfilment lifecycle. Additive to the original catalog/basket/order flow.</summary>
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;

    /// <summary>The backing PayPal payment. Null until the order is paid (authorized).</summary>
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

    /// <summary>Records a fresh authorization (hold) after PayPal accepts it. Idempotent-friendly:
    /// callers short-circuit when <see cref="Payment"/> already exists.</summary>
    public void SetAuthorized(Payment payment)
    {
        Guard.Against.Null(payment, nameof(payment));
        if (Status != OrderStatus.AwaitingPayment)
            throw new InvalidOperationException($"Order {Id} cannot be authorized from status {Status}.");
        Payment = payment;
        Status = OrderStatus.Authorized;
    }

    /// <summary>Marks the order fulfilled — the point at which the held funds are captured.</summary>
    public void MarkFulfilled()
    {
        if (Status != OrderStatus.Authorized)
            throw new InvalidOperationException($"Order {Id} cannot be fulfilled from status {Status}.");
        Status = OrderStatus.Fulfilled;
    }

    /// <summary>Marks the order cancelled after the held funds have been released.</summary>
    public void MarkCancelled()
    {
        if (Status != OrderStatus.Authorized && Status != OrderStatus.AwaitingPayment)
            throw new InvalidOperationException($"Order {Id} cannot be cancelled from status {Status}.");
        if (Payment != null) Payment.MarkAuthorizationVoided();
        Status = OrderStatus.Cancelled;
    }

    /// <summary>Reflects a refund onto the order status (partial vs full).</summary>
    public void ApplyRefundState()
    {
        if (Payment == null) return;
        Status = Payment.RefundableRemaining <= 0m ? OrderStatus.Refunded : OrderStatus.PartiallyRefunded;
    }
}
