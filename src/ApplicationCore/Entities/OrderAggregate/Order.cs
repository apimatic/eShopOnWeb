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
    /// Additive fulfilment state. A newly-placed order awaits payment. Transitions are guarded so
    /// an operator can never fulfil an unpaid order, cancel a fulfilled one, or refund beyond it.
    /// </summary>
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;

    public void MarkAuthorized()
    {
        RequireStatus(OrderStatus.AwaitingPayment, "authorize payment for");
        Status = OrderStatus.PaymentAuthorized;
    }

    public void MarkFulfilled()
    {
        RequireStatus(OrderStatus.PaymentAuthorized, "fulfil");
        Status = OrderStatus.Fulfilled;
    }

    public void MarkCancelled()
    {
        if (Status is not (OrderStatus.AwaitingPayment or OrderStatus.PaymentAuthorized))
        {
            throw PaymentApiException.Conflict(
                $"Order {Id} cannot be cancelled while it is {Status}. Cancellation is only possible before fulfilment.");
        }
        Status = OrderStatus.Cancelled;
    }

    /// <summary>Reflects the refund outcome after a capture has been (partly or fully) returned.</summary>
    public void MarkRefundState(bool fullyRefunded)
    {
        if (Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded))
        {
            throw PaymentApiException.Conflict(
                $"Order {Id} cannot be refunded while it is {Status}. A refund is only possible after fulfilment.");
        }
        Status = fullyRefunded ? OrderStatus.Refunded : OrderStatus.PartiallyRefunded;
    }

    private void RequireStatus(OrderStatus expected, string action)
    {
        if (Status != expected)
        {
            throw PaymentApiException.Conflict(
                $"Order {Id} cannot {action} while it is {Status}; it must be {expected}.");
        }
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
