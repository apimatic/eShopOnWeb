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

    // Payment state. An order is placed AwaitingPayment and moves to Paid then optionally
    // Refunded as it is processed through PayPal. The provider identifiers below are references
    // to PayPal-side resources; no card data is ever stored on the order.
    public OrderPaymentStatus PaymentStatus { get; private set; } = OrderPaymentStatus.AwaitingPayment;

    /// <summary>The PayPal Orders v2 order id that captured the payment.</summary>
    public string? PaymentProviderOrderId { get; private set; }

    /// <summary>The PayPal capture id, needed to issue a refund.</summary>
    public string? PaymentCaptureId { get; private set; }

    /// <summary>The PayPal refund id, set once the order has been refunded.</summary>
    public string? PaymentRefundId { get; private set; }

    /// <summary>
    /// Records a successful PayPal capture against this order. Idempotent: calling it again once
    /// the order is already Paid is a no-op so a double-click cannot advance the order twice.
    /// </summary>
    public void MarkPaid(string providerOrderId, string captureId)
    {
        Guard.Against.NullOrEmpty(providerOrderId, nameof(providerOrderId));
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));

        if (PaymentStatus == OrderPaymentStatus.Paid)
        {
            return;
        }
        if (PaymentStatus != OrderPaymentStatus.AwaitingPayment)
        {
            throw new InvalidOperationException($"Cannot mark order {Id} as paid from status {PaymentStatus}.");
        }

        PaymentProviderOrderId = providerOrderId;
        PaymentCaptureId = captureId;
        PaymentStatus = OrderPaymentStatus.Paid;
    }

    /// <summary>
    /// Records a full refund against this order. Idempotent: calling it again once the order is
    /// already Refunded is a no-op so a double-click cannot refund twice.
    /// </summary>
    public void MarkRefunded(string refundId)
    {
        Guard.Against.NullOrEmpty(refundId, nameof(refundId));

        if (PaymentStatus == OrderPaymentStatus.Refunded)
        {
            return;
        }
        if (PaymentStatus != OrderPaymentStatus.Paid)
        {
            throw new InvalidOperationException($"Cannot refund order {Id} from status {PaymentStatus}.");
        }

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
