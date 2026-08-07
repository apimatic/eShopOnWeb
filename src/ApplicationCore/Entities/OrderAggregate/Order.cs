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
        PaymentReference = Guid.NewGuid().ToString("N");
    }

    public string BuyerId { get; private set; }

    /// <summary>
    /// A stable, globally-unique reference for this order instance, generated at creation. Payment
    /// idempotency keys derive from it, so a retried pay/refund reuses the same PayPal-Request-Id
    /// (never double-charging) while remaining unique across orders — even if the store's integer ids
    /// are reset (as the in-memory provider does on restart).
    /// </summary>
    public string PaymentReference { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }

    // --- PayPal payment state (additive; does not alter the existing cart/checkout flow) ---

    public OrderPaymentStatus PaymentStatus { get; private set; } = OrderPaymentStatus.AwaitingPayment;

    /// <summary>The PayPal order id that funded this order (reference only; no card data).</summary>
    public string? PayPalOrderId { get; private set; }

    /// <summary>The PayPal capture id — the handle used to refund this order.</summary>
    public string? PayPalCaptureId { get; private set; }

    /// <summary>The PayPal refund id, once the order has been refunded in full.</summary>
    public string? PayPalRefundId { get; private set; }

    public DateTimeOffset? PaidDate { get; private set; }
    public DateTimeOffset? RefundedDate { get; private set; }

    /// <summary>
    /// Records a completed PayPal capture against this order. Idempotent: a repeat call with the
    /// same capture id (a retried/double-clicked payment) is a no-op rather than an error.
    /// </summary>
    public void MarkPaid(string payPalOrderId, string payPalCaptureId, DateTimeOffset paidAt)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(payPalCaptureId, nameof(payPalCaptureId));

        if (PaymentStatus == OrderPaymentStatus.Paid && PayPalCaptureId == payPalCaptureId)
        {
            return; // already recorded this exact capture
        }

        if (PaymentStatus != OrderPaymentStatus.AwaitingPayment)
        {
            throw new InvalidOperationException(
                $"Order {Id} cannot be marked paid from state {PaymentStatus}.");
        }

        PayPalOrderId = payPalOrderId;
        PayPalCaptureId = payPalCaptureId;
        PaidDate = paidAt;
        PaymentStatus = OrderPaymentStatus.Paid;
    }

    /// <summary>
    /// Records a completed full refund. Idempotent: repeating it with the same refund id is a no-op.
    /// </summary>
    public void MarkRefunded(string payPalRefundId, DateTimeOffset refundedAt)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));

        if (PaymentStatus == OrderPaymentStatus.Refunded && PayPalRefundId == payPalRefundId)
        {
            return; // already recorded this exact refund
        }

        if (PaymentStatus != OrderPaymentStatus.Paid)
        {
            throw new InvalidOperationException(
                $"Order {Id} cannot be refunded from state {PaymentStatus}; only a paid order can be refunded.");
        }

        PayPalRefundId = payPalRefundId;
        RefundedDate = refundedAt;
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
