using System;
using System.Collections.Generic;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
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

    // --- Payment state (added for PayPal integration) -------------------------------------
    // An order is placed awaiting payment and is later paid (captured) and possibly refunded
    // via PayPal. Only PayPal-issued identifiers and a safe card description are kept here;
    // full card details are never stored on the order.

    public OrderPaymentStatus PaymentStatus { get; private set; } = OrderPaymentStatus.AwaitingPayment;

    /// <summary>The PayPal Orders v2 order id created when the payment is taken.</summary>
    public string? PayPalOrderId { get; private set; }

    /// <summary>The PayPal capture id of the successful payment; used to issue a refund.</summary>
    public string? PayPalCaptureId { get; private set; }

    /// <summary>The PayPal refund id, once the payment has been refunded in full.</summary>
    public string? PayPalRefundId { get; private set; }

    /// <summary>A safe, human-readable description of the instrument used, e.g. "VISA ending 1111". Never full card details.</summary>
    public string? PaymentCardDescription { get; private set; }

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
    /// Records a successful PayPal capture and moves the order into the <see cref="OrderPaymentStatus.Paid"/> state.
    /// Only valid from <see cref="OrderPaymentStatus.AwaitingPayment"/>; the caller is responsible for
    /// short-circuiting repeat payments (idempotency) before charging.
    /// </summary>
    public void MarkPaid(string payPalOrderId, string payPalCaptureId, string cardDescription)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(payPalCaptureId, nameof(payPalCaptureId));

        if (PaymentStatus != OrderPaymentStatus.AwaitingPayment)
        {
            throw new PaymentException(
                $"Order {Id} cannot be marked paid from state {PaymentStatus}.");
        }

        PayPalOrderId = payPalOrderId;
        PayPalCaptureId = payPalCaptureId;
        PaymentCardDescription = cardDescription;
        PaidDate = DateTimeOffset.Now;
        PaymentStatus = OrderPaymentStatus.Paid;
    }

    /// <summary>
    /// Records a successful full refund and moves the order into the <see cref="OrderPaymentStatus.Refunded"/> state.
    /// Only valid from <see cref="OrderPaymentStatus.Paid"/>.
    /// </summary>
    public void MarkRefunded(string payPalRefundId)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));

        if (PaymentStatus != OrderPaymentStatus.Paid)
        {
            throw new PaymentException(
                $"Order {Id} cannot be refunded from state {PaymentStatus}.");
        }

        PayPalRefundId = payPalRefundId;
        RefundedDate = DateTimeOffset.Now;
        PaymentStatus = OrderPaymentStatus.Refunded;
    }
}
