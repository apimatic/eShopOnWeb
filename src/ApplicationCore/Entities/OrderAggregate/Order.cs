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

    // Payment state. Orders are created awaiting payment and only leave that state once PayPal
    // confirms a capture (Paid) or a refund (Refunded). Card data is NEVER stored here; only the
    // PayPal-issued identifiers needed to reconcile and refund the payment are kept.

    /// <summary>
    /// A stable, globally-unique token minted when the order is created. It is used to derive the
    /// PayPal-Request-Id (idempotency) keys for this order's pay/refund calls, so retries of the same
    /// logical operation de-duplicate at PayPal, while different orders never collide — even across
    /// process restarts where the numeric <see cref="BaseEntity.Id"/> may repeat (in-memory database).
    /// </summary>
    public Guid IdempotencyToken { get; private set; } = Guid.NewGuid();

    public PaymentStatus PaymentStatus { get; private set; } = PaymentStatus.AwaitingPayment;
    public string? PayPalOrderId { get; private set; }
    public string? PaymentCaptureId { get; private set; }
    public string? PaymentRefundId { get; private set; }

    /// <summary>
    /// Records a successful PayPal capture. Idempotent: re-marking an already-paid order with the
    /// same capture is a no-op so a double-submit cannot double-charge.
    /// </summary>
    public void MarkPaid(string payPalOrderId, string captureId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));

        if (PaymentStatus == PaymentStatus.Paid && PaymentCaptureId == captureId)
        {
            return;
        }

        if (PaymentStatus != PaymentStatus.AwaitingPayment)
        {
            throw new InvalidOperationException(
                $"Order {Id} cannot be paid because it is {PaymentStatus}.");
        }

        PayPalOrderId = payPalOrderId;
        PaymentCaptureId = captureId;
        PaymentStatus = PaymentStatus.Paid;
    }

    /// <summary>
    /// Records a successful full refund. Idempotent: re-marking an already-refunded order is a no-op
    /// so a double-submit cannot double-refund.
    /// </summary>
    public void MarkRefunded(string refundId)
    {
        Guard.Against.NullOrEmpty(refundId, nameof(refundId));

        if (PaymentStatus == PaymentStatus.Refunded)
        {
            return;
        }

        if (PaymentStatus != PaymentStatus.Paid)
        {
            throw new InvalidOperationException(
                $"Order {Id} cannot be refunded because it is {PaymentStatus}.");
        }

        PaymentRefundId = refundId;
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
