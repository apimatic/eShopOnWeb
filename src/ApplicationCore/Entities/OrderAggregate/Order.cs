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
    /// Additive payment/fulfilment lifecycle state. New orders start awaiting payment.
    /// </summary>
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;

    public void MarkAuthorized()
    {
        if (Status != OrderStatus.AwaitingPayment)
        {
            throw new PaymentDomainException(
                $"Order {Id} cannot be authorized from status {Status}; it must be {OrderStatus.AwaitingPayment}.");
        }
        Status = OrderStatus.PaymentAuthorized;
    }

    public void MarkFulfilled()
    {
        if (Status != OrderStatus.PaymentAuthorized)
        {
            throw new PaymentDomainException(
                $"Order {Id} cannot be fulfilled from status {Status}; it must be {OrderStatus.PaymentAuthorized}.");
        }
        Status = OrderStatus.Fulfilled;
    }

    public void MarkCancelled()
    {
        if (Status != OrderStatus.AwaitingPayment && Status != OrderStatus.PaymentAuthorized)
        {
            throw new PaymentDomainException(
                $"Order {Id} cannot be cancelled from status {Status}; it must be awaiting payment or authorized (cancel only applies before fulfilment).");
        }
        Status = OrderStatus.Cancelled;
    }

    /// <summary>
    /// Records the outcome of a refund against a fulfilled order.
    /// </summary>
    /// <param name="fullyRefunded">true when the cumulative refunded amount equals the captured amount.</param>
    public void MarkRefunded(bool fullyRefunded)
    {
        if (Status != OrderStatus.Fulfilled && Status != OrderStatus.PartiallyRefunded)
        {
            throw new PaymentDomainException(
                $"Order {Id} cannot be refunded from status {Status}; only a fulfilled order can be refunded.");
        }
        Status = fullyRefunded ? OrderStatus.Refunded : OrderStatus.PartiallyRefunded;
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
