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

    /// <summary>The payment/fulfilment lifecycle state. New orders await payment.</summary>
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;

    /// <summary>The PayPal payment attached to this order once it has been paid; null while awaiting payment.</summary>
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

    /// <summary>
    /// Records a successful authorization (funds held, not taken) and moves the order to
    /// <see cref="OrderStatus.Authorized"/>. Only valid while awaiting payment.
    /// </summary>
    public void MarkAuthorized(OrderPayment payment)
    {
        Guard.Against.Null(payment, nameof(payment));
        if (Status != OrderStatus.AwaitingPayment)
        {
            throw new PaymentOperationException($"Order {Id} cannot be paid because it is {Status}.");
        }
        Payment = payment;
        Status = OrderStatus.Authorized;
    }

    /// <summary>Moves the order to <see cref="OrderStatus.Fulfilled"/> after the capture has been recorded.</summary>
    public void MarkFulfilled()
    {
        if (Status != OrderStatus.Authorized)
        {
            throw new PaymentOperationException($"Order {Id} cannot be fulfilled because it is {Status}.");
        }
        Status = OrderStatus.Fulfilled;
    }

    /// <summary>Moves the order to <see cref="OrderStatus.Cancelled"/> after the hold has been voided.</summary>
    public void MarkCancelled()
    {
        if (Status != OrderStatus.Authorized)
        {
            throw new PaymentOperationException($"Order {Id} cannot be cancelled because it is {Status}. Cancellation is only possible before fulfilment.");
        }
        Payment?.MarkVoided();
        Status = OrderStatus.Cancelled;
    }

    /// <summary>
    /// Reflects a refund against the captured payment: fully refunded ⇒ <see cref="OrderStatus.Refunded"/>,
    /// otherwise <see cref="OrderStatus.PartiallyRefunded"/>. Called after the refund is added to the payment.
    /// </summary>
    public void ReflectRefundState()
    {
        if (Payment is null)
        {
            return;
        }
        Status = Payment.RefundableRemaining() <= 0m ? OrderStatus.Refunded : OrderStatus.PartiallyRefunded;
    }
}
