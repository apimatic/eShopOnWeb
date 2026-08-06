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

    // Payment state (additive). A newly placed order awaits payment; PayPal identifiers are
    // recorded as the order moves through pay/refund so the flow is idempotent and auditable.
    public PaymentStatus PaymentStatus { get; private set; } = PaymentStatus.AwaitingPayment;

    /// <summary>The PayPal Orders v2 order id created when the payment was captured.</summary>
    public string? PaymentOrderId { get; private set; }

    /// <summary>The PayPal capture id, used as the target for a later refund.</summary>
    public string? PaymentCaptureId { get; private set; }

    /// <summary>The PayPal refund id, set once the order has been fully refunded.</summary>
    public string? PaymentRefundId { get; private set; }

    /// <summary>
    /// Records a successful payment capture. Idempotent: calling it again once the order is
    /// already paid is a no-op, so a duplicate request never records a second charge.
    /// </summary>
    public void MarkAsPaid(string paymentOrderId, string paymentCaptureId)
    {
        Guard.Against.NullOrEmpty(paymentOrderId, nameof(paymentOrderId));
        Guard.Against.NullOrEmpty(paymentCaptureId, nameof(paymentCaptureId));

        if (PaymentStatus == PaymentStatus.Paid) return;
        if (PaymentStatus != PaymentStatus.AwaitingPayment)
            throw new InvalidOperationException($"Cannot pay an order in state {PaymentStatus}.");

        PaymentOrderId = paymentOrderId;
        PaymentCaptureId = paymentCaptureId;
        PaymentStatus = PaymentStatus.Paid;
    }

    /// <summary>
    /// Records a full refund. Idempotent: calling it again once refunded is a no-op, so a
    /// duplicate request never records a second refund. Only a paid order can be refunded.
    /// </summary>
    public void MarkAsRefunded(string paymentRefundId)
    {
        Guard.Against.NullOrEmpty(paymentRefundId, nameof(paymentRefundId));

        if (PaymentStatus == PaymentStatus.Refunded) return;
        if (PaymentStatus != PaymentStatus.Paid)
            throw new InvalidOperationException($"Cannot refund an order in state {PaymentStatus}.");

        PaymentRefundId = paymentRefundId;
        PaymentStatus = PaymentStatus.Refunded;
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
