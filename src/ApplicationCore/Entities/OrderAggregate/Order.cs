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
    /// Where the order sits in the payment/fulfilment lifecycle. Defaults to
    /// <see cref="OrderStatus.AwaitingPayment"/> for a freshly placed order.
    /// </summary>
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;

    public void MarkPaymentAuthorized()
    {
        RequireStatus(OrderStatus.AwaitingPayment, OrderStatus.PaymentAuthorized);
        Status = OrderStatus.PaymentAuthorized;
    }

    public void MarkFulfilled()
    {
        RequireStatus(OrderStatus.PaymentAuthorized, OrderStatus.Fulfilled);
        Status = OrderStatus.Fulfilled;
    }

    public void MarkCancelled()
    {
        RequireStatus(OrderStatus.AwaitingPayment, OrderStatus.PaymentAuthorized, OrderStatus.Cancelled);
        Status = OrderStatus.Cancelled;
    }

    public void MarkRefunded(bool fully)
    {
        RequireStatus(OrderStatus.Fulfilled, OrderStatus.PartiallyRefunded, OrderStatus.Refunded);
        Status = fully ? OrderStatus.Refunded : OrderStatus.PartiallyRefunded;
    }

    private void RequireStatus(params OrderStatus[] allowed)
    {
        if (Array.IndexOf(allowed, Status) < 0)
        {
            throw new InvalidOperationException(
                $"Order {Id} is '{Status}'; this operation requires one of: {string.Join(", ", allowed)}.");
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
