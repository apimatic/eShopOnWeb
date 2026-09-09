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

    /// <summary>Where this order sits in the payment / fulfilment lifecycle.</summary>
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;

    /// <summary>The PayPal payment attached to this order, once <c>pay</c> has been called.</summary>
    public OrderPayment? Payment { get; private set; }

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
    /// Attach the authorization (hold) obtained from PayPal. Only valid while the order
    /// is still awaiting payment; once authorized, a repeated pay is a no-op (idempotent)
    /// handled by the caller.
    /// </summary>
    public void RecordAuthorization(OrderPayment payment)
    {
        Guard.Against.Null(payment, nameof(payment));

        if (Status is OrderStatus.Fulfilled or OrderStatus.Cancelled)
            throw new PaymentConflictException($"Order {Id} is {Status} and can no longer be paid.");

        Payment = payment;
        Status = OrderStatus.PaymentAuthorized;
    }

    /// <summary>Record that the held funds were captured at fulfilment.</summary>
    public void RecordFulfilment(string captureId, string captureStatus, decimal grossAmount, decimal payPalFee, decimal netAmount)
    {
        RequirePayment();

        if (Status == OrderStatus.Fulfilled)
            throw new PaymentConflictException($"Order {Id} has already been fulfilled.");

        if (Status != OrderStatus.PaymentAuthorized)
            throw new PaymentConflictException($"Order {Id} is {Status}; it must be authorized before it can be fulfilled.");

        Payment!.RecordCapture(captureId, captureStatus, grossAmount, payPalFee, netAmount);
        Status = OrderStatus.Fulfilled;
    }

    /// <summary>Record that a stale hold was renewed before fulfilment.</summary>
    public void RecordReauthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        RequirePayment();
        Payment!.RenewAuthorization(authorizationId, authorizationStatus, expiresAt);
    }

    /// <summary>
    /// Cancel the order before fulfilment. If a hold exists it is expected to have been
    /// released (voided) with PayPal by the caller before this is invoked.
    /// </summary>
    public void RecordCancellation()
    {
        if (Status == OrderStatus.Fulfilled)
            throw new PaymentConflictException(
                $"Order {Id} has been fulfilled; funds were already captured. Issue a refund instead of a cancellation.");

        Payment?.MarkVoided();
        Status = OrderStatus.Cancelled;
    }

    /// <summary>Record a refund against the captured payment, after guarding it is legitimate.</summary>
    public OrderRefund RecordRefund(string payPalRefundId, decimal amount, string status, string idempotencyKey)
    {
        RequirePayment();
        return Payment!.AddRefund(payPalRefundId, amount, status, idempotencyKey);
    }

    private void RequirePayment()
    {
        if (Payment is null)
            throw new PaymentConflictException($"Order {Id} has no payment; it has not been paid yet.");
    }
}
