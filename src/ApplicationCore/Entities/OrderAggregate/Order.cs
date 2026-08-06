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

    // --- Payment state (additive) ---------------------------------------------------------------
    // An order is placed AwaitingPayment. Paying with PayPal moves it to Paid and records the
    // provider identifiers; a full refund moves it to Refunded. Full card details are never held
    // here — only PayPal's own reference ids.

    public OrderPaymentStatus PaymentStatus { get; private set; } = OrderPaymentStatus.AwaitingPayment;

    /// <summary>ISO-4217 currency the order is priced and charged in. Fixed to USD for this app.</summary>
    public string Currency { get; private set; } = "USD";

    /// <summary>Name of the payment provider that processed the order (e.g. "PayPal").</summary>
    public string? PaymentProvider { get; private set; }

    /// <summary>The PayPal order id created when the payment was taken.</summary>
    public string? PaymentProviderOrderId { get; private set; }

    /// <summary>The PayPal capture id that moved the funds; the target of a later refund.</summary>
    public string? PaymentCaptureId { get; private set; }

    /// <summary>The PayPal refund id, once the capture has been refunded in full.</summary>
    public string? PaymentRefundId { get; private set; }

    public DateTimeOffset? PaidDate { get; private set; }
    public DateTimeOffset? RefundedDate { get; private set; }

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
    /// Records a completed PayPal capture against this order. Idempotent for the same capture id:
    /// re-recording the capture that already paid the order is a no-op rather than an error, so a
    /// replayed request never double-marks. Marking a *different* capture on an already-paid order
    /// is rejected.
    /// </summary>
    public void MarkPaid(string providerOrderId, string captureId, string provider = "PayPal")
    {
        Guard.Against.NullOrEmpty(providerOrderId, nameof(providerOrderId));
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));

        if (PaymentStatus == OrderPaymentStatus.Paid)
        {
            if (PaymentCaptureId == captureId)
            {
                return; // idempotent replay of the same capture
            }
            throw new InvalidOperationException($"Order {Id} is already paid by capture {PaymentCaptureId}.");
        }

        if (PaymentStatus == OrderPaymentStatus.Refunded)
        {
            throw new InvalidOperationException($"Order {Id} has been refunded and cannot be paid again.");
        }

        PaymentProvider = provider;
        PaymentProviderOrderId = providerOrderId;
        PaymentCaptureId = captureId;
        PaidDate = DateTimeOffset.UtcNow;
        PaymentStatus = OrderPaymentStatus.Paid;
    }

    /// <summary>
    /// Records a completed full refund. Idempotent for the same refund id: re-recording the refund
    /// that already refunded the order is a no-op. A paid order is required.
    /// </summary>
    public void MarkRefunded(string refundId)
    {
        Guard.Against.NullOrEmpty(refundId, nameof(refundId));

        if (PaymentStatus == OrderPaymentStatus.Refunded)
        {
            if (PaymentRefundId == refundId)
            {
                return; // idempotent replay of the same refund
            }
            throw new InvalidOperationException($"Order {Id} has already been refunded by refund {PaymentRefundId}.");
        }

        if (PaymentStatus != OrderPaymentStatus.Paid)
        {
            throw new InvalidOperationException($"Order {Id} is not paid and cannot be refunded.");
        }

        PaymentRefundId = refundId;
        RefundedDate = DateTimeOffset.UtcNow;
        PaymentStatus = OrderPaymentStatus.Refunded;
    }
}
