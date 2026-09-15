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

    // --- Additive payment/fulfilment state (does not replace the existing order flow) ---

    /// <summary>The fulfilment lifecycle of this order. New orders start awaiting payment.</summary>
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;

    /// <summary>
    /// The payment for this order, or null while the order is still awaiting payment. Owned by the
    /// aggregate, so it loads and saves together with the order.
    /// </summary>
    public OrderPayment? Payment { get; private set; }

    /// <summary>
    /// Attach an authorized payment to this order. Idempotent callers must check <see cref="Status"/>
    /// first; this guards that an order is only authorized once.
    /// </summary>
    public void AuthorizePayment(OrderPayment payment)
    {
        Guard.Against.Null(payment, nameof(payment));
        if (Status != OrderStatus.AwaitingPayment)
        {
            throw new OrderPaymentException(
                $"Order {Id} cannot be authorized because it is {Status}.");
        }

        Payment = payment;
        Status = OrderStatus.PaymentAuthorized;
    }

    /// <summary>Mark the order fulfilled. The money is captured by the caller before this is set.</summary>
    public void MarkFulfilled()
    {
        if (Status != OrderStatus.PaymentAuthorized)
        {
            throw new OrderPaymentException(
                $"Order {Id} cannot be fulfilled because it is {Status}. Only an authorized order can be fulfilled.");
        }

        Status = OrderStatus.Fulfilled;
    }

    /// <summary>
    /// Cancel the order before fulfilment. Any held funds are released by the caller (void) first.
    /// A fulfilled order cannot be cancelled — it must be refunded instead.
    /// </summary>
    public void Cancel()
    {
        if (Status == OrderStatus.Fulfilled)
        {
            throw new OrderPaymentException(
                $"Order {Id} has already been fulfilled and cannot be cancelled; issue a refund instead.");
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
