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
        PaymentStatus = OrderPaymentStatus.PendingPayment;
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

    /// <summary>Where this order sits in the payment/fulfilment lifecycle.</summary>
    public OrderPaymentStatus PaymentStatus { get; private set; }

    /// <summary>The PayPal-owned payment state; null until the order is authorized.</summary>
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

    // --- Payment lifecycle transitions -------------------------------------------------

    /// <summary>Record a successful authorization (money held, not captured).</summary>
    public void RecordAuthorization(string currency, string payPalOrderId, string authorizationId,
        string? authorizationStatus, DateTimeOffset authorizedAt, DateTimeOffset? expiresAt, string customReference)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(customReference, nameof(customReference));

        Payment ??= new OrderPayment(currency);
        Payment.SetAuthorization(payPalOrderId, authorizationId, authorizationStatus, authorizedAt, expiresAt, customReference);
        PaymentStatus = OrderPaymentStatus.Authorized;
    }

    /// <summary>Record that a stale hold was renewed with a fresh authorization.</summary>
    public void RecordReauthorization(string authorizationId, string? authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.Null(Payment, nameof(Payment));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Payment!.ReplaceAuthorization(authorizationId, authorizationStatus, expiresAt);
    }

    /// <summary>Record the capture taken at fulfilment (money actually moved).</summary>
    public void RecordCapture(string captureId, string? captureStatus, decimal capturedAmount,
        decimal? paypalFee, decimal? netAmount, DateTimeOffset capturedAt)
    {
        Guard.Against.Null(Payment, nameof(Payment));
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        Payment!.SetCapture(captureId, captureStatus, capturedAmount, paypalFee, netAmount, capturedAt);
        PaymentStatus = OrderPaymentStatus.Paid;
    }

    /// <summary>Cancel an authorized-but-not-captured order: the hold was voided at PayPal.</summary>
    public void RecordVoid()
    {
        Guard.Against.Null(Payment, nameof(Payment));
        Payment!.MarkVoided();
        PaymentStatus = OrderPaymentStatus.Cancelled;
    }

    /// <summary>Cancel an order that never took a hold — nothing to release at PayPal.</summary>
    public void CancelBeforeAuthorization()
    {
        PaymentStatus = OrderPaymentStatus.Cancelled;
    }

    /// <summary>Record a (full or partial) refund against the capture and update the status.</summary>
    public void RecordRefund(string refundId, decimal amount, string status, string idempotencyKey, DateTimeOffset createdAt)
    {
        Guard.Against.Null(Payment, nameof(Payment));
        var refund = new OrderRefund(refundId, amount, status, idempotencyKey, createdAt);
        Payment!.AddRefund(refund);

        PaymentStatus = Payment.RemainingRefundable() <= 0m
            ? OrderPaymentStatus.Refunded
            : OrderPaymentStatus.PartiallyRefunded;
    }
}
