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

    // --- Payment / fulfilment state (additive) ---

    /// <summary>Where this order sits in the payment lifecycle.</summary>
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;

    /// <summary>The money side of the order. Null until the order is paid (authorized).</summary>
    public Payment? Payment { get; private set; }

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
    /// Attach a freshly created PayPal authorization (the hold) and move the order to
    /// <see cref="OrderStatus.Authorized"/>.
    /// </summary>
    public void SetAuthorized(Payment payment)
    {
        if (Status != OrderStatus.AwaitingPayment)
        {
            throw new PaymentException($"Order {Id} cannot be authorized because it is {Status}.");
        }
        Payment = payment;
        Status = OrderStatus.Authorized;
    }

    /// <summary>Fulfil the order: record the capture PayPal reported. Stays a one-way move to Fulfilled.</summary>
    public void SetFulfilled(string captureId, string captureStatus, decimal capturedAmount, decimal? payPalFee, decimal? netAmount)
    {
        if (Status != OrderStatus.Authorized)
        {
            throw new PaymentException($"Order {Id} cannot be fulfilled because it is {Status}.");
        }
        Guard.Against.Null(Payment, nameof(Payment));
        Payment!.RecordCapture(captureId, captureStatus, capturedAmount, payPalFee, netAmount);
        Status = OrderStatus.Fulfilled;
    }

    /// <summary>Cancel before fulfilment: the authorization has been voided, so no money moved.</summary>
    public void SetCancelled()
    {
        if (Status != OrderStatus.Authorized)
        {
            throw new PaymentException($"Order {Id} cannot be cancelled because it is {Status}. Cancellation is only possible before fulfilment.");
        }
        Guard.Against.Null(Payment, nameof(Payment));
        Payment!.RecordVoid();
        Status = OrderStatus.Cancelled;
    }

    /// <summary>
    /// Guards a refund: only fulfilled (or partly refunded) orders can be refunded, and never for
    /// more than the amount still refundable against the capture.
    /// </summary>
    public void EnsureCanRefund(decimal amount)
    {
        if (Status != OrderStatus.Fulfilled && Status != OrderStatus.PartiallyRefunded)
        {
            throw new PaymentException($"Order {Id} cannot be refunded because it is {Status}. Only fulfilled orders can be refunded.");
        }
        Guard.Against.Null(Payment, nameof(Payment));
        if (amount <= 0m)
        {
            throw new PaymentException("Refund amount must be greater than zero.");
        }
        if (amount > Payment!.RefundableRemaining)
        {
            throw new PaymentException(
                $"Refund of {amount:0.00} exceeds the refundable remaining {Payment.RefundableRemaining:0.00} on order {Id}.");
        }
    }

    /// <summary>Record a completed refund and update the order status to partially/fully refunded.</summary>
    public void RecordRefund(PaymentRefund refund)
    {
        Guard.Against.Null(Payment, nameof(Payment));
        Payment!.AddRefund(refund);
        Status = Payment.RefundableRemaining <= 0m
            ? OrderStatus.Refunded
            : OrderStatus.PartiallyRefunded;
    }
}
