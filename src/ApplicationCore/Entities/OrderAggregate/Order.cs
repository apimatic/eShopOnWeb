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

    /// <summary>Where this order sits in the payment / fulfilment lifecycle.</summary>
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;

    /// <summary>The money-movement record. Null until the order is paid.</summary>
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

    /// <summary>Attach the hold placed at pay-time and move the order to Authorized.</summary>
    public void SetAuthorized(Payment payment)
    {
        Guard.Against.Null(payment, nameof(payment));
        Payment = payment;
        Status = OrderStatus.Authorized;
    }

    /// <summary>The money was taken at fulfilment.</summary>
    public void SetFulfilled() => Status = OrderStatus.Fulfilled;

    /// <summary>The held funds were released before fulfilment; no money moved.</summary>
    public void SetCancelled() => Status = OrderStatus.Cancelled;

    /// <summary>Recompute the refund state after a return against the capture.</summary>
    public void SetRefundState()
    {
        if (Payment == null || !Payment.IsCaptured)
        {
            return;
        }

        var refunded = Payment.TotalRefunded();
        if (refunded <= 0m)
        {
            return;
        }

        Status = refunded >= (Payment.CapturedAmount ?? 0m)
            ? OrderStatus.Refunded
            : OrderStatus.PartiallyRefunded;
    }
}
