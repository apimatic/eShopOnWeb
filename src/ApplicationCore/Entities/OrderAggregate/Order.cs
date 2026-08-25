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
        Status = OrderStatus.AwaitingPayment;
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public OrderStatus Status { get; private set; }
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

    /// <summary>
    /// Starts (or resumes) payment on this order, creating the payment tracking record the first time it's called.
    /// </summary>
    public OrderPayment BeginPayment(string currencyCode)
    {
        if (Status != OrderStatus.AwaitingPayment)
        {
            throw new OrderPaymentStateException($"Order {Id} is not awaiting payment (current status: {Status}).");
        }

        Payment ??= new OrderPayment(Id, Total(), currencyCode);
        return Payment;
    }

    public void MarkPaymentAuthorized()
    {
        if (Status != OrderStatus.AwaitingPayment)
        {
            throw new OrderPaymentStateException($"Order {Id} cannot be marked as authorized from status {Status}.");
        }

        Status = OrderStatus.PaymentAuthorized;
    }

    public void MarkFulfilled()
    {
        if (Status != OrderStatus.PaymentAuthorized)
        {
            throw new OrderPaymentStateException($"Order {Id} cannot be fulfilled from status {Status}; it must have an authorized payment.");
        }

        Status = OrderStatus.Fulfilled;
    }

    public void MarkCancelled()
    {
        if (Status != OrderStatus.AwaitingPayment && Status != OrderStatus.PaymentAuthorized)
        {
            throw new OrderPaymentStateException($"Order {Id} cannot be cancelled from status {Status}; it has already been fulfilled.");
        }

        Status = OrderStatus.Cancelled;
    }

    public void MarkRefunded(bool isFullRefund)
    {
        if (Status != OrderStatus.Fulfilled && Status != OrderStatus.PartiallyRefunded)
        {
            throw new OrderPaymentStateException($"Order {Id} cannot be refunded from status {Status}; it must be fulfilled first.");
        }

        Status = isFullRefund ? OrderStatus.Refunded : OrderStatus.PartiallyRefunded;
    }
}
