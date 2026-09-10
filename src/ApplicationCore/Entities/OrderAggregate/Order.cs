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

    /// <summary>Where this order sits in the payment lifecycle. New orders await payment.</summary>
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;

    /// <summary>The payment for this order once one has been authorized. Owned by the aggregate.</summary>
    public PaymentRecord? Payment { get; private set; }

    /// <summary>
    /// Attaches the hold placed on the buyer's funds and moves the order to Authorized. Idempotent
    /// callers must check <see cref="Status"/> first; this only transitions from AwaitingPayment.
    /// </summary>
    public void AttachAuthorization(PaymentRecord payment)
    {
        Guard.Against.Null(payment, nameof(payment));
        if (Status != OrderStatus.AwaitingPayment)
            throw new InvalidOperationException($"Order {Id} cannot be authorized from status {Status}.");
        Payment = payment;
        Status = OrderStatus.Authorized;
    }

    /// <summary>Marks the order fulfilled once its payment has been captured.</summary>
    public void MarkFulfilled()
    {
        if (Status != OrderStatus.Authorized)
            throw new InvalidOperationException($"Order {Id} cannot be fulfilled from status {Status}.");
        if (Payment is null || !Payment.IsCaptured)
            throw new InvalidOperationException($"Order {Id} has no captured payment to fulfil.");
        Status = OrderStatus.Paid;
    }

    /// <summary>Marks the order cancelled after its hold has been released. Only valid before fulfilment.</summary>
    public void MarkCancelled()
    {
        if (Status is OrderStatus.Paid)
            throw new InvalidOperationException($"Order {Id} has been fulfilled and cannot be cancelled; refund instead.");
        if (Status is OrderStatus.Cancelled)
            return;
        Status = OrderStatus.Cancelled;
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
