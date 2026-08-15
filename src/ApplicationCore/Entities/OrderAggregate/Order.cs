using System;
using System.Collections.Generic;
using System.Linq;
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

    public decimal Total()
    {
        var total = 0m;
        foreach (var item in _orderItems)
        {
            total += item.UnitPrice * item.Units;
        }
        return total;
    }

    // ---------------------------------------------------------------------------------------------
    // Payment / fulfilment state (additive). This carries enough of the state PayPal owns — the ids
    // and current status of the hold, the capture and the refunds — that a later request can act on
    // the order, not only the one that started it. No card details are ever stored here.
    // ---------------------------------------------------------------------------------------------

    public OrderPaymentStatus PaymentStatus { get; private set; } = OrderPaymentStatus.AwaitingPayment;

    /// <summary>ISO currency code the payment was taken in (from configuration, echoed back by PayPal).</summary>
    public string? PaymentCurrency { get; private set; }

    /// <summary>The PayPal Order (checkout) id created when authorizing.</summary>
    public string? PayPalOrderId { get; private set; }

    /// <summary>The current PayPal authorization id (the hold). Replaced if the hold is reauthorized.</summary>
    public string? PayPalAuthorizationId { get; private set; }

    /// <summary>The PayPal capture id created at fulfilment.</summary>
    public string? PayPalCaptureId { get; private set; }

    /// <summary>When the current authorization goes stale. Used to decide whether it must be renewed.</summary>
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    /// <summary>The saved card used to pay, if any (null for a one-off card).</summary>
    public int? SavedPaymentMethodId { get; private set; }

    // Settlement figures reported by PayPal at capture.
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    private readonly List<OrderRefund> _refunds = new List<OrderRefund>();
    public IReadOnlyCollection<OrderRefund> Refunds => _refunds.AsReadOnly();

    /// <summary>True once a hold exists that has not yet been captured or released.</summary>
    public bool IsAwaitingPayment => PaymentStatus == OrderPaymentStatus.AwaitingPayment;

    public decimal TotalRefunded() => _refunds.Sum(r => r.Amount);

    /// <summary>How much of the captured amount can still be refunded. Never negative.</summary>
    public decimal RefundableRemaining()
    {
        var captured = CapturedAmount ?? 0m;
        var remaining = captured - TotalRefunded();
        return remaining > 0m ? remaining : 0m;
    }

    /// <summary>Record that PayPal is holding the order total. Idempotent callers should check
    /// <see cref="IsAwaitingPayment"/> before invoking this.</summary>
    public void SetAuthorized(string payPalOrderId, string authorizationId, string currency,
        DateTimeOffset? expiresAt, int? savedPaymentMethodId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        if (PaymentStatus != OrderPaymentStatus.AwaitingPayment)
        {
            throw new OrderPaymentException(
                $"Order {Id} cannot be authorized because it is '{PaymentStatus}'.");
        }

        PayPalOrderId = payPalOrderId;
        PayPalAuthorizationId = authorizationId;
        PaymentCurrency = currency;
        AuthorizationExpiresAt = expiresAt;
        SavedPaymentMethodId = savedPaymentMethodId;
        PaymentStatus = OrderPaymentStatus.Authorized;
    }

    /// <summary>Swap in a fresh authorization after the previous hold went stale and was renewed.</summary>
    public void SetReauthorized(string authorizationId, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        if (PaymentStatus != OrderPaymentStatus.Authorized)
        {
            throw new OrderPaymentException(
                $"Order {Id} cannot be reauthorized because it is '{PaymentStatus}'.");
        }

        PayPalAuthorizationId = authorizationId;
        AuthorizationExpiresAt = expiresAt;
    }

    /// <summary>Record that the money was taken at fulfilment, with what PayPal reported.</summary>
    public void SetFulfilled(string captureId, decimal capturedAmount, decimal? payPalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));

        if (PaymentStatus != OrderPaymentStatus.Authorized)
        {
            throw new OrderPaymentException(
                $"Order {Id} cannot be fulfilled because it is '{PaymentStatus}'.");
        }

        PayPalCaptureId = captureId;
        CapturedAmount = capturedAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        PaymentStatus = OrderPaymentStatus.Fulfilled;
    }

    /// <summary>Cancel before fulfilment — the hold was released, no money moved.</summary>
    public void SetCancelled()
    {
        if (PaymentStatus != OrderPaymentStatus.Authorized &&
            PaymentStatus != OrderPaymentStatus.AwaitingPayment)
        {
            throw new OrderPaymentException(
                $"Order {Id} cannot be cancelled because it is '{PaymentStatus}'. " +
                "Only an order that has not been fulfilled can be cancelled; use a refund instead.");
        }

        PaymentStatus = OrderPaymentStatus.Cancelled;
    }

    /// <summary>Add a refund against the captured payment. Enforces that the order never becomes
    /// refundable beyond what was captured.</summary>
    public OrderRefund AddRefund(string idempotencyKey, string payPalRefundId, decimal amount, string status)
    {
        if (PaymentStatus != OrderPaymentStatus.Fulfilled &&
            PaymentStatus != OrderPaymentStatus.PartiallyRefunded)
        {
            throw new OrderPaymentException(
                $"Order {Id} cannot be refunded because it is '{PaymentStatus}'. " +
                "Only a fulfilled order can be refunded.");
        }

        if (amount > RefundableRemaining())
        {
            throw new OrderPaymentException(
                $"Refund of {amount} exceeds the remaining refundable amount {RefundableRemaining()} " +
                $"for order {Id}.");
        }

        var refund = new OrderRefund(idempotencyKey, payPalRefundId, amount, status);
        _refunds.Add(refund);

        PaymentStatus = RefundableRemaining() <= 0m
            ? OrderPaymentStatus.Refunded
            : OrderPaymentStatus.PartiallyRefunded;

        return refund;
    }

    /// <summary>Find an already-recorded refund by its idempotency key (for idempotent replays).</summary>
    public OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
}
