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

    // --- Payment state (PayPal integration) -------------------------------------------------
    // Card data is never stored here; only PayPal's opaque resource identifiers are kept so the
    // order can be paid, reconciled and refunded. An order is born awaiting payment.

    public PaymentStatus PaymentStatus { get; private set; } = PaymentStatus.AwaitingPayment;

    /// <summary>The PayPal Orders v2 order id created when payment is attempted.</summary>
    public string? PayPalOrderId { get; private set; }

    /// <summary>The PayPal capture id — the handle a refund is issued against.</summary>
    public string? PayPalCaptureId { get; private set; }

    /// <summary>The PayPal refund id, once the payment has been refunded.</summary>
    public string? PayPalRefundId { get; private set; }

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
    /// Records a successful PayPal capture. Only valid while the order is awaiting payment or a
    /// prior attempt failed; calling it on an already-paid order is a no-op so a double-click can
    /// never move the order past <see cref="PaymentStatus.Paid"/> twice.
    /// </summary>
    public void MarkAsPaid(string payPalOrderId, string payPalCaptureId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(payPalCaptureId, nameof(payPalCaptureId));

        if (PaymentStatus == PaymentStatus.Paid) return;

        if (PaymentStatus != PaymentStatus.AwaitingPayment && PaymentStatus != PaymentStatus.Failed)
        {
            throw new InvalidOperationException(
                $"Order {Id} cannot be marked paid from state {PaymentStatus}.");
        }

        PayPalOrderId = payPalOrderId;
        PayPalCaptureId = payPalCaptureId;
        PaymentStatus = PaymentStatus.Paid;
        PaidDate = DateTimeOffset.Now;
    }

    /// <summary>Records a failed payment attempt so the shopper may retry.</summary>
    public void MarkPaymentFailed(string? payPalOrderId)
    {
        if (PaymentStatus == PaymentStatus.Paid || PaymentStatus == PaymentStatus.Refunded) return;

        if (!string.IsNullOrEmpty(payPalOrderId))
        {
            PayPalOrderId = payPalOrderId;
        }
        PaymentStatus = PaymentStatus.Failed;
    }

    /// <summary>
    /// Records a full refund of this order's capture. Only valid for a paid order; calling it on an
    /// already-refunded order is a no-op so a double-click can never issue a second refund.
    /// </summary>
    public void MarkAsRefunded(string payPalRefundId)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));

        if (PaymentStatus == PaymentStatus.Refunded) return;

        if (PaymentStatus != PaymentStatus.Paid)
        {
            throw new InvalidOperationException(
                $"Order {Id} cannot be refunded from state {PaymentStatus}.");
        }

        PayPalRefundId = payPalRefundId;
        PaymentStatus = PaymentStatus.Refunded;
        RefundedDate = DateTimeOffset.Now;
    }
}
