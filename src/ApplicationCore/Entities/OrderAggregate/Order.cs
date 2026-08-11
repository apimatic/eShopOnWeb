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

    /// <summary>
    /// A stable, globally-unique reference for this order's payment, minted when the order is
    /// created and persisted with it. Used to derive idempotency keys and PayPal references that
    /// stay stable across retries yet never collide with a different order — even one that happens
    /// to reuse the same database id after an in-memory restart.
    /// </summary>
    public Guid PaymentReference { get; private set; } = Guid.NewGuid();

    /// <summary>Where this order sits in the payment/fulfilment lifecycle.</summary>
    public OrderStatus Status { get; private set; }

    /// <summary>The money side of the order. Null until the shopper pays (authorizes).</summary>
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

    // ---- Lifecycle transitions ----

    /// <summary>Attaches the authorized payment (money held) and moves the order to Authorized.</summary>
    public void SetAuthorized(Payment payment)
    {
        Guard.Against.Null(payment, nameof(payment));
        if (Status != OrderStatus.AwaitingPayment && Status != OrderStatus.Authorized)
        {
            throw new InvalidOperationException($"Order {Id} cannot be paid from status {Status}.");
        }
        Payment = payment;
        Status = OrderStatus.Authorized;
    }

    /// <summary>Marks the order fulfilled once the held funds have been captured.</summary>
    public void MarkFulfilled()
    {
        if (Status != OrderStatus.Authorized)
        {
            throw new InvalidOperationException($"Order {Id} cannot be fulfilled from status {Status}.");
        }
        Status = OrderStatus.Fulfilled;
    }

    /// <summary>Marks the order cancelled after the hold has been released. Only valid before fulfilment.</summary>
    public void MarkCancelled()
    {
        if (Status != OrderStatus.Authorized && Status != OrderStatus.AwaitingPayment)
        {
            throw new InvalidOperationException($"Order {Id} cannot be cancelled from status {Status}.");
        }
        Status = OrderStatus.Cancelled;
    }

    /// <summary>Reflects the payment's refund state onto the order after a refund settles.</summary>
    public void SyncRefundState()
    {
        if (Payment is null) return;
        Status = Payment.Status switch
        {
            PaymentStatus.Refunded => OrderStatus.Refunded,
            PaymentStatus.PartiallyRefunded => OrderStatus.PartiallyRefunded,
            _ => Status
        };
    }
}
