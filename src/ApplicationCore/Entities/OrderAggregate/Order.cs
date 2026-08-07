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

    // --- Payment state (PayPal) -------------------------------------------------------------
    // Additive to the original one-time-commerce model: an order tracks whether it has been paid
    // for and, if so, the PayPal identifiers required to reconcile and later refund the payment.
    // No card details are ever held here — only PayPal-generated, non-sensitive identifiers.
    public PaymentStatus PaymentStatus { get; private set; } = PaymentStatus.AwaitingPayment;

    /// <summary>The PayPal Checkout order id that funded this payment.</summary>
    public string? PayPalOrderId { get; private set; }

    /// <summary>The PayPal capture id, used as the target for a later refund.</summary>
    public string? PayPalCaptureId { get; private set; }

    /// <summary>The PayPal refund id, set once the payment has been refunded.</summary>
    public string? PayPalRefundId { get; private set; }

    // Idempotency keys tied to this order's payment/refund. Generated once and persisted, then reused
    // for any retry (e.g. a double-click), so PayPal de-duplicates a retried operation at the source.
    // Being per-order GUIDs, they are globally unique and never collide across orders or restarts.
    public string? PaymentIdempotencyKey { get; private set; }
    public string? RefundIdempotencyKey { get; private set; }

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
    /// Returns this order's payment idempotency key, generating and storing one on first use so that
    /// every retry of the same order's payment reuses it.
    /// </summary>
    public string EnsurePaymentIdempotencyKey()
    {
        PaymentIdempotencyKey ??= Guid.NewGuid().ToString("N");
        return PaymentIdempotencyKey;
    }

    /// <summary>
    /// Returns this order's refund idempotency key, generating and storing one on first use so that
    /// every retry of the refund reuses it.
    /// </summary>
    public string EnsureRefundIdempotencyKey()
    {
        RefundIdempotencyKey ??= Guid.NewGuid().ToString("N");
        return RefundIdempotencyKey;
    }

    /// <summary>
    /// Records a successful PayPal payment for this order. The payment application service only
    /// invokes this once a capture has completed, and it will not re-invoke it for an order that
    /// is already <see cref="PaymentStatus.Paid"/>, keeping the overall pay operation idempotent.
    /// </summary>
    public void MarkAsPaid(string payPalOrderId, string captureId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));

        if (PaymentStatus == PaymentStatus.Refunded)
        {
            throw new InvalidOperationException($"Order {Id} has already been refunded and cannot be marked paid.");
        }

        PayPalOrderId = payPalOrderId;
        PayPalCaptureId = captureId;
        PaymentStatus = PaymentStatus.Paid;
    }

    /// <summary>
    /// Records a full refund of this order's captured payment.
    /// </summary>
    public void MarkAsRefunded(string refundId)
    {
        Guard.Against.NullOrEmpty(refundId, nameof(refundId));

        if (PaymentStatus != PaymentStatus.Paid)
        {
            throw new InvalidOperationException($"Order {Id} cannot be refunded because it is not in the Paid state (current: {PaymentStatus}).");
        }

        PayPalRefundId = refundId;
        PaymentStatus = PaymentStatus.Refunded;
    }
}
