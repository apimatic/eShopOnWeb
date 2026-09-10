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
    /// The fulfilment lifecycle of this order. New orders start awaiting payment. This is additive
    /// to the original eShopOnWeb model, which had no order status.
    /// </summary>
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;

    /// <summary>
    /// The payment (PayPal hold/capture/refund state) for this order, once the shopper has paid.
    /// Null while the order is still awaiting payment. Part of the Order aggregate.
    /// </summary>
    public Payment? Payment { get; private set; }

    /// <summary>Money can only be authorized while the order is still awaiting payment.</summary>
    public bool CanBeAuthorized() => Status == OrderStatus.AwaitingPayment && Payment is null;

    /// <summary>An order can be fulfilled once (and only once) its funds are held.</summary>
    public bool CanBeFulfilled() => Status == OrderStatus.PaymentAuthorized && Payment is not null;

    /// <summary>An order can be cancelled any time before it is fulfilled or already cancelled.</summary>
    public bool CanBeCancelled() => Status is OrderStatus.AwaitingPayment or OrderStatus.PaymentAuthorized;

    /// <summary>Refunds are only possible after fulfilment (once the money has actually been taken).</summary>
    public bool CanBeRefunded() => Status == OrderStatus.Fulfilled && Payment is not null;

    /// <summary>Attach the authorized payment and move the order into the authorized state.</summary>
    public void SetAuthorizedPayment(Payment payment)
    {
        Guard.Against.Null(payment, nameof(payment));
        if (!CanBeAuthorized())
        {
            throw new InvalidOperationException($"Order {Id} cannot be authorized in status {Status}.");
        }
        Payment = payment;
        Status = OrderStatus.PaymentAuthorized;
    }

    public void MarkFulfilled()
    {
        if (!CanBeFulfilled())
        {
            throw new InvalidOperationException($"Order {Id} cannot be fulfilled in status {Status}.");
        }
        Status = OrderStatus.Fulfilled;
    }

    public void MarkCancelled()
    {
        if (!CanBeCancelled())
        {
            throw new InvalidOperationException($"Order {Id} cannot be cancelled in status {Status}.");
        }
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
