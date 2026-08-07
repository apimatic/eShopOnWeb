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

    // --- Payment state (additive; the order model is reused across the app's flows) ---

    /// <summary>
    /// A stable, unique reference for this order, generated once at creation and persisted. It seeds
    /// the idempotency keys sent to PayPal, so pay/refund retries for this order reuse the same key
    /// (no double charge/refund) while distinct orders never collide — even across app instances.
    /// </summary>
    public Guid PaymentReference { get; private set; } = Guid.NewGuid();

    /// <summary>Where this order sits in its payment lifecycle. New orders await payment.</summary>
    public OrderPaymentStatus PaymentStatus { get; private set; } = OrderPaymentStatus.AwaitingPayment;

    /// <summary>The PayPal order id created when the shopper pays. Null until a payment is attempted.</summary>
    public string? PayPalOrderId { get; private set; }

    /// <summary>The PayPal capture id for the successful payment. This is what a refund targets.</summary>
    public string? PayPalCaptureId { get; private set; }

    /// <summary>The PayPal refund id, set once the payment is fully refunded.</summary>
    public string? PayPalRefundId { get; private set; }

    /// <summary>
    /// Records a successful PayPal payment. Idempotent: calling it again for an already-paid
    /// order is a no-op, so a double-click never advances the order twice.
    /// </summary>
    public void MarkPaid(string payPalOrderId, string payPalCaptureId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(payPalCaptureId, nameof(payPalCaptureId));

        if (PaymentStatus != OrderPaymentStatus.AwaitingPayment)
        {
            return;
        }

        PayPalOrderId = payPalOrderId;
        PayPalCaptureId = payPalCaptureId;
        PaymentStatus = OrderPaymentStatus.Paid;
    }

    /// <summary>
    /// Records a full refund of this order's captured payment. Idempotent: calling it again for an
    /// already-refunded order is a no-op, so a double-click never refunds twice.
    /// </summary>
    public void MarkRefunded(string payPalRefundId)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));

        if (PaymentStatus != OrderPaymentStatus.Paid)
        {
            return;
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
