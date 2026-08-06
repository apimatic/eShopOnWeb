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

    // ---- PayPal payment state (additive) ------------------------------------
    // These properties track the order through the PayPal payment lifecycle.
    // They never hold card details - only PayPal-issued identifiers - so they
    // are safe to persist in the application's own database.

    public OrderPaymentStatus PaymentStatus { get; private set; } = OrderPaymentStatus.AwaitingPayment;

    /// <summary>Identifier of the PayPal Orders v2 order that captured the payment.</summary>
    public string? PayPalOrderId { get; private set; }

    /// <summary>Identifier of the PayPal capture (the record refunds are issued against).</summary>
    public string? PaymentCaptureId { get; private set; }

    /// <summary>Identifier of the PayPal refund, once the order has been refunded.</summary>
    public string? PaymentRefundId { get; private set; }

    /// <summary>Records a successful PayPal capture and moves the order to <see cref="OrderPaymentStatus.Paid"/>.</summary>
    public void MarkPaid(string payPalOrderId, string captureId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));

        if (PaymentStatus == OrderPaymentStatus.Refunded)
        {
            throw new System.InvalidOperationException($"Order {Id} has already been refunded and cannot be paid again.");
        }

        PayPalOrderId = payPalOrderId;
        PaymentCaptureId = captureId;
        PaymentStatus = OrderPaymentStatus.Paid;
    }

    /// <summary>Records a full PayPal refund and moves the order to <see cref="OrderPaymentStatus.Refunded"/>.</summary>
    public void MarkRefunded(string refundId)
    {
        Guard.Against.NullOrEmpty(refundId, nameof(refundId));

        if (PaymentStatus != OrderPaymentStatus.Paid)
        {
            throw new System.InvalidOperationException($"Order {Id} cannot be refunded because it is not in a paid state (current: {PaymentStatus}).");
        }

        PaymentRefundId = refundId;
        PaymentStatus = OrderPaymentStatus.Refunded;
    }
}
