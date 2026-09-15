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

    /// <summary>
    /// Where the order sits in the pay/fulfil/cancel/refund lifecycle. Additive to the classic
    /// eShop flow — orders placed the old way still default to <see cref="OrderStatus.AwaitingPayment"/>.
    /// </summary>
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;

    /// <summary>PayPal-owned payment state; null until the order is paid (authorized).</summary>
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

    /// <summary>Attaches the payment created when funds are held and moves the order to Authorized.</summary>
    public void MarkAuthorized(Payment payment)
    {
        Guard.Against.Null(payment, nameof(payment));
        Payment = payment;
        Status = OrderStatus.Authorized;
    }

    /// <summary>Marks the order fulfilled once the held funds have been captured.</summary>
    public void MarkFulfilled()
    {
        Status = OrderStatus.Fulfilled;
    }

    /// <summary>Marks the order cancelled after the held funds have been released.</summary>
    public void MarkCancelled()
    {
        Status = OrderStatus.Cancelled;
    }

    /// <summary>Reflects a refund against the captured payment onto the order status.</summary>
    public void MarkRefundApplied()
    {
        if (Payment is null) return;
        Status = Payment.RefundableRemaining() <= 0m
            ? OrderStatus.Refunded
            : OrderStatus.PartiallyRefunded;
    }
}
