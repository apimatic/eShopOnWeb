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
    public string PaymentReference { get; private set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public PaymentStatus PaymentStatus { get; private set; }
    public FulfilmentStatus FulfilmentStatus { get; private set; }
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

    public void RecordAuthorization(string paypalOrderId, string authorizationId,
        string authorizationStatus, decimal amount, string currency,
        DateTimeOffset authorizedAt, DateTimeOffset? expiresAt)
    {
        if (PaymentStatus != PaymentStatus.AwaitingPayment)
        {
            throw new InvalidOperationException("Only an order awaiting payment can be authorized.");
        }
        if (amount != Total())
        {
            throw new InvalidOperationException("The authorized amount must equal the order total.");
        }

        Payment = new OrderPayment(paypalOrderId, authorizationId, authorizationStatus,
            amount, currency, authorizedAt, expiresAt);
        PaymentStatus = PaymentStatus.Authorized;
    }

    public void RecordReauthorization(string authorizationId, string authorizationStatus,
        DateTimeOffset authorizedAt, DateTimeOffset? expiresAt)
    {
        EnsurePayment();
        if (PaymentStatus != PaymentStatus.Authorized)
        {
            throw new InvalidOperationException("Only an authorized order can be reauthorized.");
        }
        Payment!.RecordReauthorization(authorizationId, authorizationStatus, authorizedAt, expiresAt);
    }

    public void RecordCapturePending(string captureId, string captureStatus, decimal amount)
    {
        EnsurePayment();
        Payment!.RecordCapture(captureId, captureStatus, amount, null, null, null);
        PaymentStatus = PaymentStatus.CapturePending;
    }

    public void MarkFulfilled(string captureId, string captureStatus, decimal amount,
        decimal paypalFee, decimal netAmount, DateTimeOffset? capturedAt)
    {
        EnsurePayment();
        if (amount != Total())
        {
            throw new InvalidOperationException("The captured amount must equal the order total.");
        }

        Payment!.RecordCapture(captureId, captureStatus, amount, paypalFee, netAmount, capturedAt);
        PaymentStatus = PaymentStatus.Captured;
        FulfilmentStatus = FulfilmentStatus.Fulfilled;
    }

    public void MarkCancelled(string authorizationStatus, DateTimeOffset cancelledAt)
    {
        EnsurePayment();
        if (FulfilmentStatus == FulfilmentStatus.Fulfilled)
        {
            throw new InvalidOperationException("A fulfilled order cannot be cancelled.");
        }

        Payment!.RecordVoid(authorizationStatus, cancelledAt);
        PaymentStatus = PaymentStatus.Cancelled;
        FulfilmentStatus = FulfilmentStatus.Cancelled;
    }

    public PaymentRefund RecordRefund(string idempotencyKey, string paypalRefundId,
        string refundStatus, decimal amount, DateTimeOffset? createdAt)
    {
        EnsurePayment();
        if (FulfilmentStatus != FulfilmentStatus.Fulfilled)
        {
            throw new InvalidOperationException("Only a fulfilled order can be refunded.");
        }

        var refund = Payment!.AddRefund(idempotencyKey, paypalRefundId, refundStatus, amount, createdAt);
        PaymentStatus = Payment.RefundedAmount == Payment.CapturedAmount
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
        return refund;
    }

    private void EnsurePayment()
    {
        if (Payment is null)
        {
            throw new InvalidOperationException("The order has no payment.");
        }
    }
}
