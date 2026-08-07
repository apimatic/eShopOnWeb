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

    // --- Payment state -------------------------------------------------------
    // A new order awaits payment until it is paid for through PayPal, after which it may be
    // refunded in full. The PayPal identifiers below are the merchant-side references the
    // specs tell us to persist so a saved payment can later be refunded.

    public OrderPaymentStatus PaymentStatus { get; private set; } = OrderPaymentStatus.AwaitingPayment;

    /// <summary>The PayPal Checkout order id (v2 /checkout/orders) created when paying.</summary>
    public string? PayPalOrderId { get; private set; }

    /// <summary>The PayPal capture id used to issue a refund (v2 /payments/captures/{id}).</summary>
    public string? PayPalCaptureId { get; private set; }

    /// <summary>The PayPal refund id, set once the capture has been refunded.</summary>
    public string? PayPalRefundId { get; private set; }

    public DateTimeOffset? PaidDate { get; private set; }
    public DateTimeOffset? RefundedDate { get; private set; }

    /// <summary>
    /// Records a successful PayPal capture against this order. Idempotent: re-recording the
    /// same capture is a no-op so a double-click can never move the order into a bad state.
    /// </summary>
    public void MarkPaid(string payPalOrderId, string payPalCaptureId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(payPalCaptureId, nameof(payPalCaptureId));

        if (PaymentStatus == OrderPaymentStatus.Paid && PayPalCaptureId == payPalCaptureId)
        {
            return;
        }

        if (PaymentStatus != OrderPaymentStatus.AwaitingPayment)
        {
            throw new InvalidOperationException(
                $"Order {Id} cannot be paid because it is {PaymentStatus}.");
        }

        PayPalOrderId = payPalOrderId;
        PayPalCaptureId = payPalCaptureId;
        PaymentStatus = OrderPaymentStatus.Paid;
        PaidDate = DateTimeOffset.Now;
    }

    /// <summary>
    /// Records a full refund of this order's capture. Idempotent for the same refund id.
    /// </summary>
    public void MarkRefunded(string payPalRefundId)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));

        if (PaymentStatus == OrderPaymentStatus.Refunded && PayPalRefundId == payPalRefundId)
        {
            return;
        }

        if (PaymentStatus != OrderPaymentStatus.Paid)
        {
            throw new InvalidOperationException(
                $"Order {Id} cannot be refunded because it is {PaymentStatus}.");
        }

        PayPalRefundId = payPalRefundId;
        PaymentStatus = OrderPaymentStatus.Refunded;
        RefundedDate = DateTimeOffset.Now;
    }

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
}
