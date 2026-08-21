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
        PaymentStatus = OrderPaymentStatus.AwaitingPayment;
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public OrderPaymentStatus PaymentStatus { get; private set; } = OrderPaymentStatus.AwaitingPayment;
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

    public OrderPayment EnsurePayment()
    {
        Payment ??= new OrderPayment();
        return Payment;
    }

    public void MarkAuthorized()
    {
        if (PaymentStatus is OrderPaymentStatus.Fulfilled or OrderPaymentStatus.Cancelled
            or OrderPaymentStatus.Refunded or OrderPaymentStatus.PartiallyRefunded)
        {
            throw new InvalidOperationException($"Cannot authorize an order in status {PaymentStatus}.");
        }

        PaymentStatus = OrderPaymentStatus.Authorized;
    }

    public void MarkFulfilled()
    {
        if (PaymentStatus is OrderPaymentStatus.Cancelled or OrderPaymentStatus.Refunded)
        {
            throw new InvalidOperationException($"Cannot fulfil an order in status {PaymentStatus}.");
        }

        PaymentStatus = OrderPaymentStatus.Fulfilled;
    }

    public void MarkCancelled()
    {
        if (PaymentStatus is OrderPaymentStatus.Fulfilled or OrderPaymentStatus.Refunded
            or OrderPaymentStatus.PartiallyRefunded)
        {
            throw new InvalidOperationException($"Cannot cancel an order in status {PaymentStatus}.");
        }

        PaymentStatus = OrderPaymentStatus.Cancelled;
    }

    public void MarkRefunded(bool fullyRefunded)
    {
        if (PaymentStatus is not OrderPaymentStatus.Fulfilled and not OrderPaymentStatus.PartiallyRefunded
            and not OrderPaymentStatus.Refunded)
        {
            throw new InvalidOperationException($"Cannot refund an order in status {PaymentStatus}.");
        }

        PaymentStatus = fullyRefunded ? OrderPaymentStatus.Refunded : OrderPaymentStatus.PartiallyRefunded;
    }
}
