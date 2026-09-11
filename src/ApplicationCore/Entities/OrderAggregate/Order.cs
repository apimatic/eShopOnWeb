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

    // --- Payment / fulfilment state (additive) ---

    /// <summary>Fulfilment / payment lifecycle state. New orders start awaiting payment.</summary>
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;

    /// <summary>The payment (PayPal-owned state) for this order, once payment has been authorized.</summary>
    public OrderPayment? Payment { get; private set; }

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
    /// Attaches the authorized payment (a hold placed with the provider) to this order and
    /// moves it out of the awaiting-payment state. Idempotent: an order already carrying a
    /// payment is left untouched so a double-click cannot authorize twice.
    /// </summary>
    public void AttachAuthorizedPayment(OrderPayment payment)
    {
        Guard.Against.Null(payment, nameof(payment));

        if (Status != OrderStatus.AwaitingPayment)
        {
            throw new InvalidOperationException(
                $"Order {Id} cannot be paid because it is in state {Status}.");
        }

        Payment = payment;
        Status = OrderStatus.Authorized;
    }

    /// <summary>Marks the order fulfilled once the held funds have been captured.</summary>
    public void MarkFulfilled()
    {
        if (Status != OrderStatus.Authorized)
        {
            throw new InvalidOperationException(
                $"Order {Id} cannot be fulfilled because it is in state {Status}.");
        }

        Status = OrderStatus.Fulfilled;
    }

    /// <summary>Marks the order cancelled once the held funds have been released.</summary>
    public void MarkCancelled()
    {
        if (Status != OrderStatus.Authorized && Status != OrderStatus.AwaitingPayment)
        {
            throw new InvalidOperationException(
                $"Order {Id} cannot be cancelled because it is in state {Status}.");
        }

        Status = OrderStatus.Cancelled;
    }

    /// <summary>Reflects a refund against the captured payment in the order status.</summary>
    public void ReflectRefundState()
    {
        if (Payment is null)
        {
            return;
        }

        Status = Payment.RefundableRemaining() <= 0m
            ? OrderStatus.Refunded
            : OrderStatus.PartiallyRefunded;
    }

    public bool CanBeCancelled => Status == OrderStatus.Authorized || Status == OrderStatus.AwaitingPayment;
    public bool CanBeFulfilled => Status == OrderStatus.Authorized;
    public bool CanBeRefunded => Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded;
}
