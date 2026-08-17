using System;
using System.Collections.Generic;
using System.Linq;
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

    // ---------------------------------------------------------------------
    // Payment state (additive) — the money movement performed against PayPal.
    // ---------------------------------------------------------------------

    public OrderPaymentStatus PaymentStatus { get; private set; } = OrderPaymentStatus.AwaitingPayment;

    /// <summary>PayPal-owned payment state (hold/capture ids and statuses). Null until authorized.</summary>
    public PayPalPayment? Payment { get; private set; }

    private readonly List<OrderRefund> _refunds = new List<OrderRefund>();
    public IReadOnlyCollection<OrderRefund> Refunds => _refunds.AsReadOnly();

    /// <summary>Records that PayPal is now holding the funds for this order.</summary>
    public void MarkAuthorized(PayPalPayment payment)
    {
        Guard.Against.Null(payment, nameof(payment));
        Payment = payment;
        PaymentStatus = OrderPaymentStatus.Authorized;
    }

    /// <summary>Records the capture taken at fulfilment, including what PayPal reported
    /// (captured amount, fee, net proceeds).</summary>
    public void MarkCaptured(string captureId, string captureStatus, decimal capturedAmount, decimal payPalFee, decimal netAmount)
    {
        if (Payment is null)
            throw new InvalidOperationException("Cannot capture an order that was never authorized.");

        Payment.RecordCapture(captureId, captureStatus, capturedAmount, payPalFee, netAmount);
        PaymentStatus = OrderPaymentStatus.Captured;
    }

    /// <summary>Records that the hold was released before fulfilment; no money moved.</summary>
    public void MarkCancelled()
    {
        if (Payment is not null)
            Payment.SetAuthorizationStatus("VOIDED");
        PaymentStatus = OrderPaymentStatus.Cancelled;
    }

    /// <summary>Total amount refunded across all recorded refunds.</summary>
    public decimal TotalRefunded() => _refunds.Sum(r => r.Amount);

    /// <summary>How much of the captured amount can still be refunded.</summary>
    public decimal RefundableRemaining() => (Payment?.CapturedAmount ?? 0m) - TotalRefunded();

    /// <summary>Adds a refund and advances the payment status. Guards against refunding
    /// more than was captured.</summary>
    public void AddRefund(string refundId, decimal amount, string status, string idempotencyKey)
    {
        if (Payment?.CapturedAmount is null)
            throw new InvalidOperationException("Cannot refund an order that was never captured.");
        if (amount <= 0m)
            throw new ArgumentOutOfRangeException(nameof(amount), "Refund amount must be positive.");
        if (amount > RefundableRemaining())
            throw new InvalidOperationException("Refund amount exceeds the remaining refundable balance.");

        _refunds.Add(new OrderRefund(refundId, amount, status, idempotencyKey));

        PaymentStatus = TotalRefunded() >= Payment.CapturedAmount.Value
            ? OrderPaymentStatus.Refunded
            : OrderPaymentStatus.PartiallyRefunded;
    }

    /// <summary>Returns an existing refund recorded under the supplied idempotency key, if any.</summary>
    public OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
}
