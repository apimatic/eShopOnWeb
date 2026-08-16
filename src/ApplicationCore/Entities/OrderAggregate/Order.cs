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

    /// <summary>Fulfilment / payment lifecycle state. New orders start awaiting payment.</summary>
    public OrderStatus Status { get; private set; }

    /// <summary>The PayPal-backed payment for this order, once one has been started. Part of the aggregate.</summary>
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
    /// Attach a freshly created payment to this order (before authorization is placed). Idempotent:
    /// if a payment already exists it is returned unchanged, so a double-click never starts two payments.
    /// </summary>
    public Payment StartPayment(string currencyCode)
    {
        if (Payment is not null)
        {
            return Payment;
        }

        Payment = new Payment(Total(), currencyCode);
        return Payment;
    }

    /// <summary>Records that funds have been authorized (held). Requires an order awaiting payment.</summary>
    public void MarkAuthorized()
    {
        if (Status != OrderStatus.AwaitingPayment && Status != OrderStatus.PaymentAuthorized)
        {
            throw new InvalidOperationException($"Cannot authorize an order in status {Status}.");
        }
        Status = OrderStatus.PaymentAuthorized;
    }

    /// <summary>Records that the order was fulfilled and the authorization captured (money taken).</summary>
    public void MarkFulfilled()
    {
        if (Status != OrderStatus.PaymentAuthorized)
        {
            throw new InvalidOperationException($"Cannot fulfil an order in status {Status}; it must be authorized first.");
        }
        Status = OrderStatus.Fulfilled;
    }

    /// <summary>Cancels an order before fulfilment, releasing any held funds.</summary>
    public void MarkCancelled()
    {
        if (Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            throw new InvalidOperationException($"Cannot cancel an order in status {Status}; it has already been fulfilled.");
        }
        Status = OrderStatus.Cancelled;
    }

    /// <summary>Recomputes status after a refund, based on how much of the capture remains.</summary>
    public void ApplyRefundOutcome()
    {
        if (Payment is null || !Payment.IsCaptured)
        {
            throw new InvalidOperationException("Cannot refund an order that has not been captured.");
        }

        Status = Payment.RefundableRemaining() <= 0m
            ? OrderStatus.Refunded
            : OrderStatus.PartiallyRefunded;
    }

    public bool CanBeCancelled => Status is OrderStatus.AwaitingPayment or OrderStatus.PaymentAuthorized;
    public bool CanBeFulfilled => Status is OrderStatus.PaymentAuthorized;
    public bool CanBeRefunded => Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded;
}
