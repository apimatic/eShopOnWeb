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

    // --- Payment / fulfilment state (additive) ------------------------------------------------
    // An order is placed AwaitingPayment; the money is held at /pay (PaymentAuthorized),
    // taken at /fulfil (Fulfilled), released at /cancel (Cancelled) or returned at /refunds.
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;

    /// <summary>The PayPal-owned payment state for this order. Null until the order is paid.</summary>
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
    /// Ensures a <see cref="OrderPayment"/> exists for the given currency and amount, returning it.
    /// The amount is fixed to the order total to the cent, so PayPal always holds exactly the total.
    /// </summary>
    public OrderPayment EnsurePayment(string currency, decimal amount)
    {
        Payment ??= new OrderPayment(currency, amount);
        return Payment;
    }

    public void MarkAuthorized() => Status = OrderStatus.PaymentAuthorized;

    public void MarkFulfilled() => Status = OrderStatus.Fulfilled;

    public void MarkCancelled() => Status = OrderStatus.Cancelled;

    /// <summary>
    /// Recomputes the order status after a refund, based on how much of the captured amount remains.
    /// </summary>
    public void RefreshRefundStatus()
    {
        if (Payment is null || !Payment.HasCapture)
        {
            return;
        }

        if (Payment.RefundableRemaining <= 0.0001m)
        {
            Status = OrderStatus.Refunded;
        }
        else if (Payment.RefundedAmount > 0m)
        {
            Status = OrderStatus.PartiallyRefunded;
        }
    }
}
