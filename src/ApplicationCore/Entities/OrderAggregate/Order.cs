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

    public OrderPayment BeginPaymentAuthorization(string authorizationRequestId)
    {
        if (Status != OrderStatus.AwaitingPayment)
        {
            throw new InvalidOrderStateException($"Order {Id} is {Status} and cannot be authorized for payment.");
        }

        Payment = new OrderPayment(Id, authorizationRequestId);
        return Payment;
    }

    public void CompletePaymentAuthorization(string? payPalOrderId, string authorizationId, string status, decimal amount, string currencyCode, DateTimeOffset? expiresAt)
    {
        Guard.Against.Null(Payment, nameof(Payment));
        Payment!.RecordAuthorization(payPalOrderId, authorizationId, status, amount, currencyCode, expiresAt);
        Status = OrderStatus.PaymentAuthorized;
    }

    public void MarkCancelled()
    {
        if (Status != OrderStatus.AwaitingPayment && Status != OrderStatus.PaymentAuthorized)
        {
            throw new InvalidOrderStateException($"Order {Id} is {Status} and can no longer be cancelled.");
        }

        Payment?.RecordVoid();
        Status = OrderStatus.Cancelled;
    }

    public void MarkFulfilled(string captureId, string status, decimal grossAmount, decimal feeAmount, decimal netAmount)
    {
        if (Status != OrderStatus.PaymentAuthorized)
        {
            throw new InvalidOrderStateException($"Order {Id} is {Status} and cannot be fulfilled.");
        }

        Guard.Against.Null(Payment, nameof(Payment));
        Payment!.RecordCapture(captureId, status, grossAmount, feeAmount, netAmount);
        Status = OrderStatus.Fulfilled;
    }

    public OrderPaymentRefund RecordRefund(string payPalRefundId, decimal amount, string status, string idempotencyKey)
    {
        if (Status != OrderStatus.Fulfilled && Status != OrderStatus.PartiallyRefunded)
        {
            throw new InvalidOrderStateException($"Order {Id} is {Status} and has nothing captured to refund.");
        }

        Guard.Against.Null(Payment, nameof(Payment));
        var refund = Payment!.AddRefund(payPalRefundId, amount, status, idempotencyKey);
        Status = Payment.TotalRefunded >= Payment.CapturedAmount
            ? OrderStatus.Refunded
            : OrderStatus.PartiallyRefunded;
        return refund;
    }
}
