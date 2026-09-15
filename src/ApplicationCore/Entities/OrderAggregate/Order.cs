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
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;
    public FulfilmentStatus FulfilmentStatus { get; private set; } = FulfilmentStatus.Unfulfilled;
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

    public void InitializePayment(string currency)
    {
        Payment ??= new OrderPayment(currency);
    }

    public void MarkAuthorized()
    {
        if (FulfilmentStatus != FulfilmentStatus.Unfulfilled)
        {
            throw new InvalidOperationException("Only an unfulfilled order can be authorized.");
        }

        Status = OrderStatus.Authorized;
    }

    public void MarkFulfilled()
    {
        if (Status != OrderStatus.Authorized)
        {
            throw new InvalidOperationException("Only an authorized order can be fulfilled.");
        }

        Status = OrderStatus.Fulfilled;
        FulfilmentStatus = FulfilmentStatus.Fulfilled;
    }

    public void MarkCancelled()
    {
        if (FulfilmentStatus == FulfilmentStatus.Fulfilled)
        {
            throw new InvalidOperationException("A fulfilled order cannot be cancelled.");
        }

        Status = OrderStatus.Cancelled;
        FulfilmentStatus = FulfilmentStatus.Cancelled;
    }

    public void RequireNewPayment()
    {
        if (FulfilmentStatus != FulfilmentStatus.Unfulfilled)
        {
            throw new InvalidOperationException("This order cannot accept another payment.");
        }

        Status = OrderStatus.AwaitingPayment;
        Payment?.BeginNewAuthorizationAttempt();
    }

    public void UpdateRefundStatus(decimal completedRefundAmount)
    {
        if (Payment?.CapturedAmount is null || completedRefundAmount <= 0)
        {
            return;
        }

        Status = completedRefundAmount >= Payment.CapturedAmount.Value
            ? OrderStatus.Refunded
            : OrderStatus.PartiallyRefunded;
    }
}
