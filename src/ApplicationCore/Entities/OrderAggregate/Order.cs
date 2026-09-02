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

    public Order(string buyerId, Address shipToAddress, List<OrderItem> items, string? currency = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        BuyerId = buyerId;
        ShipToAddress = shipToAddress;
        Currency = currency;
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

    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;

    /// <summary>ISO-4217 currency the order total is expressed and charged in.</summary>
    public string? Currency { get; private set; }

    // ---- PayPal-owned payment state (ids + statuses so later requests can act on them) ----

    public string? PayPalOrderId { get; private set; }
    public string? PayPalAuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public decimal? AuthorizedAmount { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }

    public string? PayPalCaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    /// <summary>Safe description of the card used to pay (brand + last digits only, never a PAN).</summary>
    public string? PaymentCardBrand { get; private set; }
    public string? PaymentCardLastDigits { get; private set; }

    /// <summary>Number of payment attempts; used to derive unique PayPal idempotency keys per attempt.</summary>
    public int PaymentAttempts { get; private set; }

    private readonly List<PaymentRefund> _refunds = new List<PaymentRefund>();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal Total()
    {
        var total = 0m;
        foreach (var item in _orderItems)
        {
            total += item.UnitPrice * item.Units;
        }
        return total;
    }

    public decimal TotalRefunded() => _refunds.Sum(r => r.Amount);

    public decimal RefundableAmount() => (CapturedAmount ?? 0m) - TotalRefunded();

    public int BeginPaymentAttempt()
    {
        if (Status != OrderStatus.AwaitingPayment)
        {
            throw new InvalidPaymentStateException($"Order {Id} cannot be paid while in state {Status}.");
        }
        PaymentAttempts++;
        return PaymentAttempts;
    }

    public void MarkAuthorized(string payPalOrderId, string authorizationId, string authorizationStatus,
        decimal amount, DateTimeOffset? expiresAt, string? cardBrand, string? cardLastDigits)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        PayPalAuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizedAmount = amount;
        AuthorizationExpiresAt = expiresAt;
        AuthorizedAt = DateTimeOffset.UtcNow;
        PaymentCardBrand = cardBrand;
        PaymentCardLastDigits = cardLastDigits;
        Status = OrderStatus.PaymentAuthorized;
    }

    public void MarkAuthorizationRenewed(string authorizationId, string authorizationStatus, decimal amount, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalAuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizedAmount = amount;
        AuthorizationExpiresAt = expiresAt;
        AuthorizedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Lets the shopper pay again after a denied/voided authorization.</summary>
    public void MarkPaymentFailed(string? authorizationStatus)
    {
        AuthorizationStatus = authorizationStatus;
        PayPalAuthorizationId = null;
        AuthorizedAmount = null;
        AuthorizationExpiresAt = null;
        Status = OrderStatus.AwaitingPayment;
    }

    public void MarkCaptured(string captureId, string captureStatus, decimal grossAmount, decimal? payPalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        if (Status != OrderStatus.PaymentAuthorized)
        {
            throw new InvalidPaymentStateException($"Order {Id} cannot be captured while in state {Status}.");
        }

        PayPalCaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = grossAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        CapturedAt = DateTimeOffset.UtcNow;
        AuthorizationStatus = "CAPTURED";
        Status = OrderStatus.Fulfilled;
    }

    public void MarkCancelled()
    {
        if (Status != OrderStatus.AwaitingPayment && Status != OrderStatus.PaymentAuthorized)
        {
            throw new InvalidPaymentStateException($"Order {Id} cannot be cancelled while in state {Status}.");
        }

        if (Status == OrderStatus.PaymentAuthorized)
        {
            AuthorizationStatus = "VOIDED";
        }
        Status = OrderStatus.Cancelled;
    }

    public PaymentRefund AddRefund(string payPalRefundId, string refundStatus, decimal amount, string currency, string idempotencyKey, string? noteToPayer)
    {
        if (Status != OrderStatus.Fulfilled)
        {
            throw new InvalidPaymentStateException($"Order {Id} cannot be refunded while in state {Status}.");
        }
        if (amount > RefundableAmount())
        {
            throw new RefundExceedsCapturedException(
                $"Refund of {amount} {currency} exceeds the refundable remainder {RefundableAmount()} {currency} of order {Id}.");
        }

        var refund = new PaymentRefund(payPalRefundId, refundStatus, amount, currency, idempotencyKey, noteToPayer);
        _refunds.Add(refund);
        CaptureStatus = RefundableAmount() <= 0m ? "REFUNDED" : "PARTIALLY_REFUNDED";
        return refund;
    }
}
