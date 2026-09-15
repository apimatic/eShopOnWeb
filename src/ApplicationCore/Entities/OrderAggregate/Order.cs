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
        PaymentStatus = PaymentStatus.AwaitingPayment;
        FulfilmentStatus = FulfilmentStatus.Pending;
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public PaymentStatus PaymentStatus { get; private set; }
    public FulfilmentStatus FulfilmentStatus { get; private set; }

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

    public void MarkAuthorized()
    {
        if (FulfilmentStatus != FulfilmentStatus.Pending || PaymentStatus != PaymentStatus.AwaitingPayment)
        {
            throw new InvalidOperationException("Only an unpaid, pending order can be authorized.");
        }

        PaymentStatus = PaymentStatus.Authorized;
    }

    public void MarkFulfilled()
    {
        if (FulfilmentStatus != FulfilmentStatus.Pending || PaymentStatus != PaymentStatus.Authorized)
        {
            throw new InvalidOperationException("Only an authorized, pending order can be fulfilled.");
        }

        PaymentStatus = PaymentStatus.Captured;
        FulfilmentStatus = FulfilmentStatus.Fulfilled;
    }

    public void MarkCancelled()
    {
        if (FulfilmentStatus != FulfilmentStatus.Pending || PaymentStatus == PaymentStatus.Captured ||
            PaymentStatus == PaymentStatus.PartiallyRefunded || PaymentStatus == PaymentStatus.Refunded)
        {
            throw new InvalidOperationException("A fulfilled order cannot be cancelled; refund it instead.");
        }

        PaymentStatus = PaymentStatus.Voided;
        FulfilmentStatus = FulfilmentStatus.Cancelled;
    }

    public void MarkRefunded(bool isFullRefund)
    {
        if (FulfilmentStatus != FulfilmentStatus.Fulfilled ||
            PaymentStatus is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded))
        {
            throw new InvalidOperationException("Only a captured payment can be refunded.");
        }

        PaymentStatus = isFullRefund ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
    }
}

public enum PaymentStatus
{
    AwaitingPayment,
    Authorized,
    Captured,
    PartiallyRefunded,
    Refunded,
    Voided
}

public enum FulfilmentStatus
{
    Pending,
    Fulfilled,
    Cancelled
}
