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
        PaymentReference = Guid.NewGuid();
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }

    /// <summary>
    /// A stable, globally-unique reference for this order, generated once when the order is placed.
    /// It is used to derive deterministic payment/refund idempotency keys for PayPal so a retried or
    /// double-clicked operation is de-duplicated — while never colliding across app instances or runs
    /// (unlike the sequential <see cref="BaseEntity.Id"/>, which restarts from 1 on an in-memory store).
    /// </summary>
    public Guid PaymentReference { get; private set; }

    // Payment state. An order is created awaiting payment and is only ever charged/refunded through
    // the methods below, so the aggregate owns its own state transitions. No raw card data is ever
    // stored here — only PayPal's own resource identifiers, which are safe to persist.
    public OrderPaymentStatus PaymentStatus { get; private set; } = OrderPaymentStatus.AwaitingPayment;

    /// <summary>Id of the PayPal Orders v2 resource that captured the payment.</summary>
    public string? PayPalOrderId { get; private set; }

    /// <summary>Id of the PayPal capture — required to later issue a refund.</summary>
    public string? PayPalCaptureId { get; private set; }

    /// <summary>Id of the PayPal refund, once the order has been refunded.</summary>
    public string? PayPalRefundId { get; private set; }

    /// <summary>
    /// Records a successful PayPal capture against this order. Only valid while the order is still
    /// awaiting payment; a paid or refunded order is never charged again (idempotency is enforced at
    /// the application boundary before this is called).
    /// </summary>
    public void MarkPaid(string payPalOrderId, string payPalCaptureId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(payPalCaptureId, nameof(payPalCaptureId));

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
    /// Records a full refund of this order's captured payment. Only valid for a paid order.
    /// </summary>
    public void MarkRefunded(string payPalRefundId)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));

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
