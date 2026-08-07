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

    // --- Payment state ---------------------------------------------------------------------------
    // eShopOnWeb has no payment processing out of the box. These fields track the PayPal payment
    // for this order. Only PayPal-generated identifiers are stored here; card details never are.

    public OrderPaymentStatus PaymentStatus { get; private set; } = OrderPaymentStatus.AwaitingPayment;

    /// <summary>Identifier of the PayPal Checkout order (v2/checkout/orders) that captured the payment.</summary>
    public string? PayPalOrderId { get; private set; }

    /// <summary>Identifier of the PayPal capture (used to issue refunds).</summary>
    public string? PaymentCaptureId { get; private set; }

    /// <summary>Identifier of the PayPal refund, once the order has been refunded.</summary>
    public string? PaymentRefundId { get; private set; }

    public DateTimeOffset? PaidDate { get; private set; }
    public DateTimeOffset? RefundedDate { get; private set; }

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
    /// Records a successful PayPal capture against this order. Idempotent: if the same capture has
    /// already been recorded the call is a no-op, so a duplicated request never double-transitions.
    /// </summary>
    public void MarkAsPaid(string payPalOrderId, string captureId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));

        if (PaymentStatus == OrderPaymentStatus.Paid && PaymentCaptureId == captureId)
        {
            return;
        }

        if (PaymentStatus == OrderPaymentStatus.Refunded)
        {
            throw new InvalidOperationException($"Order {Id} has already been refunded and cannot be marked paid.");
        }

        PayPalOrderId = payPalOrderId;
        PaymentCaptureId = captureId;
        PaymentStatus = OrderPaymentStatus.Paid;
        PaidDate = DateTimeOffset.Now;
    }

    /// <summary>
    /// Records a successful full refund against this order. Idempotent for the same refund id.
    /// </summary>
    public void MarkAsRefunded(string refundId)
    {
        Guard.Against.NullOrEmpty(refundId, nameof(refundId));

        if (PaymentStatus == OrderPaymentStatus.Refunded && PaymentRefundId == refundId)
        {
            return;
        }

        if (PaymentStatus != OrderPaymentStatus.Paid)
        {
            throw new InvalidOperationException($"Order {Id} cannot be refunded because it is not in a paid state (current: {PaymentStatus}).");
        }

        PaymentRefundId = refundId;
        PaymentStatus = OrderPaymentStatus.Refunded;
        RefundedDate = DateTimeOffset.Now;
    }
}
