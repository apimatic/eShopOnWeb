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

    public decimal Total()
    {
        var total = 0m;
        foreach (var item in _orderItems)
        {
            total += item.UnitPrice * item.Units;
        }
        return total;
    }

    // ---- Payment / fulfilment state (additive) -------------------------------------------------

    /// <summary>Where this order sits in the payment/fulfilment lifecycle.</summary>
    public PaymentStatus PaymentStatus { get; private set; } = PaymentStatus.AwaitingPayment;

    /// <summary>The PayPal-owned payment state, once the order has been paid (authorized). Null while awaiting payment.</summary>
    public Payment? Payment { get; private set; }

    /// <summary>Record that the order total has been authorized (held) at PayPal.</summary>
    public void Authorize(Payment payment)
    {
        Guard.Against.Null(payment, nameof(payment));
        if (PaymentStatus != PaymentStatus.AwaitingPayment)
        {
            throw new InvalidOperationException(
                $"Order {Id} cannot be authorized from status {PaymentStatus}.");
        }
        Payment = payment;
        PaymentStatus = PaymentStatus.Authorized;
    }

    /// <summary>Replace the stored hold with a renewed authorization (used when it went stale before fulfilment).</summary>
    public void RenewAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        RequirePayment().RenewAuthorization(authorizationId, authorizationStatus, expiresAt);
    }

    /// <summary>Record that the hold was captured at fulfilment — money taken.</summary>
    public void Fulfil(string captureId, string captureStatus, decimal capturedAmount, decimal? payPalFee, decimal? netAmount)
    {
        if (PaymentStatus != PaymentStatus.Authorized)
        {
            throw new InvalidOperationException(
                $"Order {Id} cannot be fulfilled from status {PaymentStatus}; it must be Authorized.");
        }
        RequirePayment().RecordCapture(captureId, captureStatus, capturedAmount, payPalFee, netAmount);
        PaymentStatus = PaymentStatus.Paid;
    }

    /// <summary>Cancel before fulfilment: the hold has been released, no money moved.</summary>
    public void Cancel()
    {
        if (PaymentStatus != PaymentStatus.Authorized)
        {
            throw new InvalidOperationException(
                $"Order {Id} cannot be cancelled from status {PaymentStatus}; only an authorized (unfulfilled) order can be cancelled.");
        }
        RequirePayment().MarkVoided();
        PaymentStatus = PaymentStatus.Cancelled;
    }

    /// <summary>Record a refund against the captured payment, adjusting the order status accordingly.</summary>
    public void AddRefund(PaymentRefund refund)
    {
        if (PaymentStatus is not (PaymentStatus.Paid or PaymentStatus.PartiallyRefunded))
        {
            throw new InvalidOperationException(
                $"Order {Id} cannot be refunded from status {PaymentStatus}; it must be fulfilled (Paid) first.");
        }
        var payment = RequirePayment();
        payment.AddRefund(refund);
        PaymentStatus = payment.RefundedAmount >= (payment.CapturedAmount ?? 0m)
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
    }

    private Payment RequirePayment() =>
        Payment ?? throw new InvalidOperationException($"Order {Id} has no payment recorded.");
}
