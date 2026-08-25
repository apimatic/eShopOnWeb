using System;
using System.Collections.Generic;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
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
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;
    public OrderPayment? Payment { get; private set; }

    /// <summary>
    /// A per-order random seed used to build PayPal idempotency keys (PayPal-Request-Id). PayPal retains those
    /// keys for up to 45 days; the database's own Order.Id is not safe to use directly because the in-memory
    /// provider (and any fresh restore) restarts numbering from 1, which would collide with a stale PayPal-side
    /// idempotent response from a previous, unrelated order that happened to get the same numeric id.
    /// </summary>
    public Guid PaymentIdempotencySeed { get; private set; } = Guid.NewGuid();

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

    /// <summary>Attaches the authorized PayPal payment hold to this order. Only valid from AwaitingPayment.</summary>
    public void AttachPayment(OrderPayment payment)
    {
        if (Status != OrderStatus.AwaitingPayment)
            throw new InvalidOrderStateException(Id, Status, "attach a payment authorization");

        Payment = payment;
        Status = OrderStatus.PaymentAuthorized;
    }

    /// <summary>Marks the order fulfilled once its payment has been captured. Only valid from PaymentAuthorized.</summary>
    public void MarkFulfilled()
    {
        if (Status != OrderStatus.PaymentAuthorized)
            throw new InvalidOrderStateException(Id, Status, "fulfil");

        Guard.Against.Null(Payment, nameof(Payment));
        Status = OrderStatus.Fulfilled;
    }

    /// <summary>Cancels the order before fulfilment. Valid from AwaitingPayment or PaymentAuthorized.</summary>
    public void MarkCancelled()
    {
        if (Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
            throw new InvalidOrderStateException(Id, Status, "cancel (already fulfilled - use a refund instead)");

        Status = OrderStatus.Cancelled;
    }

    /// <summary>
    /// Recomputes order status after a refund has been recorded against the payment.
    /// Caller must have already added the OrderRefund to Payment before calling this.
    /// </summary>
    public void ReflectRefundState()
    {
        if (Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded))
            throw new InvalidOrderStateException(Id, Status, "refund");

        Guard.Against.Null(Payment, nameof(Payment));
        Guard.Against.Null(Payment.CapturedAmount, nameof(Payment.CapturedAmount));

        Status = Payment.TotalRefunded >= Payment.CapturedAmount.Value
            ? OrderStatus.Refunded
            : OrderStatus.PartiallyRefunded;
    }
}
