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

    // --- Payment state (PayPal). No card data is ever stored here. ---

    /// <summary>Where this order sits in its payment lifecycle.</summary>
    public OrderPaymentStatus PaymentStatus { get; private set; } = OrderPaymentStatus.AwaitingPayment;

    /// <summary>The PayPal Orders v2 id used to capture the payment.</summary>
    public string? PayPalOrderId { get; private set; }

    /// <summary>The PayPal capture id, used later to issue a refund.</summary>
    public string? PaymentCaptureId { get; private set; }

    /// <summary>The PayPal refund id, once the order has been refunded.</summary>
    public string? PaymentRefundId { get; private set; }

    /// <summary>
    /// Records a successful PayPal capture. Idempotent: safe to call again with the
    /// same capture (a repeated capture of an already-paid order is ignored). A
    /// refunded order can never be marked paid again.
    /// </summary>
    public void SetPaid(string payPalOrderId, string captureId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));

        if (PaymentStatus == OrderPaymentStatus.Refunded)
            throw new OrderPaymentException($"Order {Id} has been refunded and cannot be marked as paid.");

        PayPalOrderId = payPalOrderId;
        PaymentCaptureId = captureId;
        PaymentStatus = OrderPaymentStatus.Paid;
    }

    /// <summary>
    /// Records a successful full refund. Only a paid order may be refunded.
    /// </summary>
    public void SetRefunded(string refundId)
    {
        Guard.Against.NullOrEmpty(refundId, nameof(refundId));

        if (PaymentStatus != OrderPaymentStatus.Paid)
            throw new OrderPaymentException($"Order {Id} cannot be refunded because its payment status is {PaymentStatus}.");

        PaymentRefundId = refundId;
        PaymentStatus = OrderPaymentStatus.Refunded;
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
