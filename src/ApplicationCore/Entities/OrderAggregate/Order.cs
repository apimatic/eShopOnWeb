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

    // Payment state. An order is placed awaiting payment and only carries PayPal references once
    // it has actually been paid. The raw card is never part of the aggregate — only PayPal's
    // opaque identifiers are retained, so no cardholder data is ever persisted here.
    public PaymentStatus PaymentStatus { get; private set; } = PaymentStatus.AwaitingPayment;
    public string? PayPalOrderId { get; private set; }
    public string? PayPalCaptureId { get; private set; }
    public string? PayPalRefundId { get; private set; }

    // A stable, globally-unique token minted when the order is placed. It backs the PayPal-Request-Id for
    // this order's payment so a double-click de-duplicates at PayPal, while never colliding across app
    // restarts or other orders (unlike the auto-increment Id, which the in-memory store resets to 1 per run).
    public Guid PaymentIdempotencyToken { get; private set; } = Guid.NewGuid();

    /// <summary>
    /// Records a successful PayPal capture against this order. Idempotent: re-applying the same
    /// capture leaves the order untouched, so a retried payment can never overwrite the record.
    /// </summary>
    public void MarkPaid(string payPalOrderId, string payPalCaptureId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(payPalCaptureId, nameof(payPalCaptureId));

        if (PaymentStatus == PaymentStatus.Paid && PayPalCaptureId == payPalCaptureId)
        {
            return;
        }

        if (PaymentStatus != PaymentStatus.AwaitingPayment)
        {
            throw new InvalidOperationException(
                $"Order {Id} cannot be paid because it is {PaymentStatus}.");
        }

        PayPalOrderId = payPalOrderId;
        PayPalCaptureId = payPalCaptureId;
        PaymentStatus = PaymentStatus.Paid;
    }

    /// <summary>
    /// Records a full refund against this order. Idempotent for an already-refunded order.
    /// </summary>
    public void MarkRefunded(string payPalRefundId)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));

        if (PaymentStatus == PaymentStatus.Refunded)
        {
            return;
        }

        if (PaymentStatus != PaymentStatus.Paid)
        {
            throw new InvalidOperationException(
                $"Order {Id} cannot be refunded because it is {PaymentStatus}.");
        }

        PayPalRefundId = payPalRefundId;
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
