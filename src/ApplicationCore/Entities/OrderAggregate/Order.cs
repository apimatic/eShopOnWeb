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

    /// <summary>
    /// A stable, globally-unique reference for this order, minted at creation. Used to derive the
    /// PayPal idempotency key for authorization so a double-click never authorizes twice, while two
    /// genuinely different orders never collide (unlike the auto-increment Id, which restarts at 1
    /// with the in-memory provider).
    /// </summary>
    public string PaymentReference { get; private set; } = Guid.NewGuid().ToString("N");

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

    // ---- Payment / fulfilment state (additive; does not change the original checkout flow) ----

    /// <summary>Where the order sits in the pay → fulfil → refund lifecycle.</summary>
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;

    /// <summary>The PayPal-owned payment state. Null until the order has been paid (authorized).</summary>
    public Payment? Payment { get; private set; }

    /// <summary>Attach the PayPal authorization (money held) to the order.</summary>
    public void AttachAuthorization(Payment payment)
    {
        Guard.Against.Null(payment, nameof(payment));
        if (Status != OrderStatus.AwaitingPayment)
            throw new PaymentStateException($"Order {Id} cannot be paid because it is {Status}.");

        Payment = payment;
        Status = OrderStatus.Authorized;
    }

    /// <summary>Mark the order fulfilled — the point at which the held money is captured.</summary>
    public void MarkFulfilled()
    {
        if (Status != OrderStatus.Authorized)
            throw new PaymentStateException($"Order {Id} cannot be fulfilled because it is {Status}.");

        Status = OrderStatus.Fulfilled;
    }

    /// <summary>Cancel the order before fulfilment, releasing the held funds.</summary>
    public void MarkCancelled()
    {
        if (Status != OrderStatus.Authorized)
            throw new PaymentStateException($"Order {Id} cannot be cancelled because it is {Status}.");

        Status = OrderStatus.Cancelled;
    }

    /// <summary>Reflect the payment's refund state onto the order after a refund is recorded.</summary>
    public void ApplyRefundState()
    {
        if (Payment is null) return;

        Status = Payment.Status == PaymentStatus.Refunded
            ? OrderStatus.Refunded
            : OrderStatus.PartiallyRefunded;
    }
}
