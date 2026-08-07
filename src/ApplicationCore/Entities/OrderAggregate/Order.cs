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

    // Payment state. An order is created awaiting payment and processed through PayPal.
    // The PayPal identifiers are captured so a later refund can target the same capture,
    // and so a repeated pay/refund request can be recognised as already-applied (idempotency).
    public OrderPaymentStatus PaymentStatus { get; private set; } = OrderPaymentStatus.AwaitingPayment;
    public string? PayPalOrderId { get; private set; }
    public string? PayPalCaptureId { get; private set; }
    public string? PayPalRefundId { get; private set; }

    /// <summary>
    /// Records a successful PayPal capture against this order. Idempotent: calling it again
    /// once the order is already paid is a no-op, so a double-click cannot corrupt the state.
    /// </summary>
    public void MarkAsPaid(string payPalOrderId, string payPalCaptureId)
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
                $"Order {Id} cannot be marked paid from state {PaymentStatus}.");
        }

        PayPalOrderId = payPalOrderId;
        PayPalCaptureId = payPalCaptureId;
        PaymentStatus = OrderPaymentStatus.Paid;
    }

    /// <summary>
    /// Records a successful full refund against this order's capture. Idempotent for a repeat
    /// of the same refund.
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
            throw new InvalidOperationException(
                $"Order {Id} cannot be refunded from state {PaymentStatus}.");
        }

        PayPalRefundId = payPalRefundId;
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
