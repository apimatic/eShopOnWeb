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

    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;

    /// <summary>PayPal payment state for this order. Null until the shopper attempts to pay.</summary>
    public Payment? Payment { get; private set; }

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

    /// <summary>Begins a payment attempt and records the resulting PayPal authorization.</summary>
    public Payment BeginAuthorization(decimal amount, string currencyCode, string payPalOrderId, string authorizeRequestId, int? paymentMethodId,
        string authorizationId, string authorizationStatus, DateTimeOffset createTime, DateTimeOffset? expirationTime)
    {
        if (Status != OrderStatus.AwaitingPayment)
        {
            throw new InvalidOrderStateException(Id, Status, "authorize payment for");
        }

        var payment = new Payment(Id, amount, currencyCode, payPalOrderId, authorizeRequestId, paymentMethodId);
        payment.RecordAuthorization(authorizationId, authorizationStatus, createTime, expirationTime);
        Payment = payment;
        Status = OrderStatus.PaymentAuthorized;
        return payment;
    }

    /// <summary>Records a renewed authorization obtained because the previous one had gone stale.</summary>
    public void RecordReauthorization(string newAuthorizationId, string status, DateTimeOffset createTime, DateTimeOffset? expirationTime)
    {
        if (Payment is null || Status != OrderStatus.PaymentAuthorized)
        {
            throw new InvalidOrderStateException(Id, Status, "reauthorize payment for");
        }

        Payment.RecordReauthorization(newAuthorizationId, status, createTime, expirationTime);
    }

    /// <summary>Marks the order fulfilled and records what PayPal reported for the capture.</summary>
    public void MarkFulfilled(string captureId, string captureStatus, decimal capturedAmount, decimal feeAmount, decimal netAmount, string captureRequestId, DateTimeOffset captureTime)
    {
        if (Payment is null || Status != OrderStatus.PaymentAuthorized)
        {
            throw new InvalidOrderStateException(Id, Status, "fulfil");
        }

        Payment.RecordCapture(captureId, captureStatus, capturedAmount, feeAmount, netAmount, captureRequestId, captureTime);
        Status = OrderStatus.Fulfilled;
    }

    /// <summary>Cancels the order before fulfilment. If funds were on hold, the caller must have already
    /// voided the PayPal authorization before calling this.</summary>
    public void MarkCancelled()
    {
        if (Status != OrderStatus.AwaitingPayment && Status != OrderStatus.PaymentAuthorized)
        {
            throw new InvalidOrderStateException(Id, Status, "cancel");
        }

        Payment?.RecordVoid();
        Status = OrderStatus.Cancelled;
    }

    /// <summary>Records a refund of the captured payment, full or partial, and updates order status.</summary>
    public Refund ApplyRefund(string payPalRefundId, decimal amount, string status, string idempotencyKey, DateTimeOffset createTime)
    {
        if (Payment is null || (Status != OrderStatus.Fulfilled && Status != OrderStatus.PartiallyRefunded))
        {
            throw new InvalidOrderStateException(Id, Status, "refund");
        }

        var refund = Payment.AddRefund(payPalRefundId, amount, status, idempotencyKey, createTime);

        if (string.Equals(status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
        {
            Status = Payment.RemainingRefundableAmount <= 0m ? OrderStatus.Refunded : OrderStatus.PartiallyRefunded;
        }

        return refund;
    }
}
