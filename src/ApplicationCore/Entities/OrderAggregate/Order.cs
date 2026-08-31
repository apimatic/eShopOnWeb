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
    public string PaymentReference { get; private set; } = Guid.NewGuid().ToString("N");
    public Address ShipToAddress { get; private set; }
    public PaymentStatus PaymentStatus { get; private set; } = PaymentStatus.AwaitingPayment;
    public FulfillmentStatus FulfillmentStatus { get; private set; } = FulfillmentStatus.AwaitingPayment;
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

    public void RecordAuthorization(OrderPayment payment)
    {
        if (PaymentStatus != PaymentStatus.AwaitingPayment) throw new InvalidOperationException("Only an order awaiting payment can be authorized.");
        Payment = payment;
        PaymentStatus = PaymentStatus.Authorized;
        FulfillmentStatus = FulfillmentStatus.AwaitingFulfillment;
    }

    public void RecordCapture(string captureId, string status, decimal amount, decimal? fee,
        decimal? netAmount, DateTimeOffset capturedAt)
    {
        if (PaymentStatus != PaymentStatus.Authorized || Payment == null) throw new InvalidOperationException("The order does not have an authorization to capture.");
        Payment.RecordCapture(captureId, status, amount, fee, netAmount, capturedAt);
        PaymentStatus = PaymentStatus.Captured;
        FulfillmentStatus = FulfillmentStatus.Fulfilled;
    }

    public void Cancel(string? authorizationStatus = null)
    {
        if (FulfillmentStatus == FulfillmentStatus.Fulfilled) throw new InvalidOperationException("A fulfilled order cannot be cancelled; refund it instead.");
        if (Payment != null && authorizationStatus != null)
        {
            Payment.RecordVoid(authorizationStatus);
            PaymentStatus = PaymentStatus.Voided;
        }
        FulfillmentStatus = FulfillmentStatus.Cancelled;
    }

    public PaymentRefund RecordRefund(string idempotencyKey, string refundId, string status,
        decimal amount, DateTimeOffset createdAt)
    {
        if (Payment == null || PaymentStatus is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded)) throw new InvalidOperationException("Only a captured payment can be refunded.");
        var refund = Payment.RecordRefund(idempotencyKey, refundId, status, amount, createdAt);
        PaymentStatus = Payment.RefundedAmount == Payment.CapturedAmount ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        return refund;
    }
}
