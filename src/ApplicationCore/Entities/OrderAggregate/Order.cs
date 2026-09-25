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

    // --- Payment / fulfilment state (additive to the original order model) ---

    public OrderPaymentStatus PaymentStatus { get; private set; } = OrderPaymentStatus.AwaitingPayment;

    /// <summary>The PayPal-owned payment state for this order. Null until payment is first attempted.</summary>
    public OrderPayment? Payment { get; private set; }

    /// <summary>Creates (once) the payment record that will hold PayPal's ids and status.</summary>
    public OrderPayment BeginPayment(string currencyCode, string referenceId, decimal authorizedAmount)
    {
        Payment ??= new OrderPayment(currencyCode, referenceId, authorizedAmount);
        return Payment;
    }

    public void RecordAuthorization(string payPalOrderId, string authorizationId, string? status, DateTimeOffset? expiresAt)
    {
        EnsurePaymentStarted();
        Payment!.RecordPayPalOrder(payPalOrderId);
        Payment!.RecordAuthorization(authorizationId, status, expiresAt);
        PaymentStatus = OrderPaymentStatus.Authorized;
    }

    /// <summary>Records a renewed hold (reauthorization) without changing the order's lifecycle status.</summary>
    public void RecordReauthorization(string authorizationId, string? status, DateTimeOffset? expiresAt)
    {
        EnsurePaymentStarted();
        Payment!.RecordAuthorization(authorizationId, status, expiresAt);
    }

    public void RecordCapture(string captureId, string? status, decimal? capturedAmount, decimal? paypalFee, decimal? netAmount)
    {
        EnsurePaymentStarted();
        Payment!.RecordCapture(captureId, status, capturedAmount, paypalFee, netAmount);
        PaymentStatus = OrderPaymentStatus.Fulfilled;
    }

    public void RecordCancellation()
    {
        EnsurePaymentStarted();
        Payment!.MarkVoided();
        PaymentStatus = OrderPaymentStatus.Cancelled;
    }

    public void RecordRefund(string idempotencyKey, string payPalRefundId, decimal amount, string status)
    {
        EnsurePaymentStarted();
        Payment!.AddRefund(idempotencyKey, payPalRefundId, amount, status);
        PaymentStatus = Payment!.TotalRefunded >= (Payment!.CapturedAmount ?? 0m)
            ? OrderPaymentStatus.Refunded
            : OrderPaymentStatus.PartiallyRefunded;
    }

    private void EnsurePaymentStarted()
    {
        if (Payment is null)
        {
            throw new System.InvalidOperationException("Order payment has not been started.");
        }
    }
}
