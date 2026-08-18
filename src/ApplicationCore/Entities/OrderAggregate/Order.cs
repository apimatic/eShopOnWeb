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

    /// <summary>Where this order sits in the payment/fulfilment lifecycle.</summary>
    public OrderPaymentStatus PaymentStatus { get; private set; } = OrderPaymentStatus.AwaitingPayment;

    /// <summary>PayPal payment state; null until the order is paid (authorized).</summary>
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

    /// <summary>Record the hold placed on the shopper's funds. Only valid while awaiting payment.</summary>
    public void RecordAuthorization(OrderPayment payment)
    {
        Guard.Against.Null(payment, nameof(payment));
        if (PaymentStatus != OrderPaymentStatus.AwaitingPayment)
        {
            throw new PaymentException($"Order {Id} cannot be paid because it is {PaymentStatus}.");
        }

        Payment = payment;
        PaymentStatus = OrderPaymentStatus.Authorized;
    }

    /// <summary>Mark the order fulfilled once the capture has been recorded on the payment.</summary>
    public void RecordFulfilment()
    {
        if (PaymentStatus != OrderPaymentStatus.Authorized)
        {
            throw new PaymentException($"Order {Id} cannot be fulfilled because it is {PaymentStatus}.");
        }

        PaymentStatus = OrderPaymentStatus.Fulfilled;
    }

    /// <summary>Cancel the order before fulfilment, after its held funds have been released.</summary>
    public void RecordCancellation()
    {
        if (PaymentStatus != OrderPaymentStatus.Authorized)
        {
            throw new PaymentException($"Order {Id} cannot be cancelled because it is {PaymentStatus}.");
        }

        PaymentStatus = OrderPaymentStatus.Cancelled;
    }

    /// <summary>Record a refund against the captured payment and update the order's refund state.</summary>
    public void RecordRefund(PaymentRefund refund)
    {
        Guard.Against.Null(refund, nameof(refund));
        if (Payment is null || PaymentStatus is not (OrderPaymentStatus.Fulfilled or OrderPaymentStatus.PartiallyRefunded))
        {
            throw new PaymentException($"Order {Id} cannot be refunded because it is {PaymentStatus}.");
        }

        Payment.AddRefund(refund);
        PaymentStatus = Payment.RefundableRemaining() <= 0m
            ? OrderPaymentStatus.Refunded
            : OrderPaymentStatus.PartiallyRefunded;
    }
}
