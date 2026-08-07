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

    // --- Payment state (added for the PayPal integration) ---------------------------------
    // An order is created AwaitingPayment and transitions to Paid once a PayPal capture completes,
    // then optionally to Refunded. We persist only the PayPal identifiers needed to reconcile and
    // refund the payment - never card data.
    public OrderPaymentStatus PaymentStatus { get; private set; } = OrderPaymentStatus.AwaitingPayment;

    /// <summary>The PayPal order (v2 checkout order) id created when charging.</summary>
    public string? PayPalOrderId { get; private set; }

    /// <summary>The PayPal capture id - required to issue a refund.</summary>
    public string? PayPalCaptureId { get; private set; }

    /// <summary>The PayPal refund id, set once refunded.</summary>
    public string? PayPalRefundId { get; private set; }

    public DateTimeOffset? PaidAt { get; private set; }
    public DateTimeOffset? RefundedAt { get; private set; }

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

    /// <summary>
    /// Records a successful PayPal capture against this order. Idempotent: re-recording the same
    /// capture is a no-op so a double-click can never move an already-paid order twice.
    /// </summary>
    public void MarkAsPaid(string payPalOrderId, string payPalCaptureId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(payPalCaptureId, nameof(payPalCaptureId));

        if (PaymentStatus == OrderPaymentStatus.Paid && PayPalCaptureId == payPalCaptureId)
        {
            return;
        }

        if (PaymentStatus == OrderPaymentStatus.Refunded)
        {
            throw new InvalidOperationException($"Order {Id} has already been refunded and cannot be marked paid.");
        }

        PayPalOrderId = payPalOrderId;
        PayPalCaptureId = payPalCaptureId;
        PaymentStatus = OrderPaymentStatus.Paid;
        PaidAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Marks that a payment attempt failed. Leaves the order payable again.</summary>
    public void MarkPaymentFailed()
    {
        if (PaymentStatus == OrderPaymentStatus.Paid || PaymentStatus == OrderPaymentStatus.Refunded)
        {
            return;
        }

        PaymentStatus = OrderPaymentStatus.Failed;
    }

    /// <summary>
    /// Records a successful full refund. Idempotent: re-recording the same refund is a no-op.
    /// </summary>
    public void MarkAsRefunded(string payPalRefundId)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));

        if (PaymentStatus == OrderPaymentStatus.Refunded)
        {
            return;
        }

        if (PaymentStatus != OrderPaymentStatus.Paid)
        {
            throw new InvalidOperationException($"Order {Id} cannot be refunded because it is not paid (status: {PaymentStatus}).");
        }

        PayPalRefundId = payPalRefundId;
        PaymentStatus = OrderPaymentStatus.Refunded;
        RefundedAt = DateTimeOffset.UtcNow;
    }
}
