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

    private readonly List<PaymentRefund> _paymentRefunds = new List<PaymentRefund>();
    public IReadOnlyCollection<PaymentRefund> PaymentRefunds => _paymentRefunds.AsReadOnly();

    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;
    public PaymentStatus PaymentStatus { get; private set; } = PaymentStatus.NotStarted;

    /// <summary>
    /// Stable reference for the current payment attempt. Used as the PayPal invoice id
    /// and as the basis for PayPal idempotency keys, so retrying the same logical
    /// payment operation never charges the shopper twice. A new reference is issued
    /// only when a brand-new payment attempt is started.
    /// </summary>
    public string? PaymentReference { get; private set; }

    public string? Currency { get; private set; }
    public string? PayPalOrderId { get; private set; }
    public string? PayPalAuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? PayPalCaptureId { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public decimal RefundedAmount { get; private set; }

    public decimal Total()
    {
        var total = 0m;
        foreach (var item in _orderItems)
        {
            total += item.UnitPrice * item.Units;
        }
        return total;
    }

    public decimal RefundableAmount()
    {
        return (CapturedAmount ?? 0m) - RefundedAmount;
    }

    public void SetCurrency(string currency)
    {
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Currency = currency;
    }

    /// <summary>
    /// Starts a new payment attempt, issuing a fresh payment reference. Only valid while
    /// the order is still awaiting payment; a paid-for order cannot be re-paid.
    /// </summary>
    public void BeginPaymentAttempt()
    {
        if (Status != OrderStatus.AwaitingPayment)
        {
            throw new InvalidOperationException($"Order {Id} cannot be paid while in status {Status}.");
        }
        if (PaymentStatus == PaymentStatus.NotStarted && PaymentReference is not null)
        {
            // A previous attempt was interrupted before its outcome was known; reusing the
            // same reference lets PayPal de-duplicate the retry via the idempotency key.
            return;
        }
        PaymentReference = Guid.NewGuid().ToString("N");
        PaymentStatus = PaymentStatus.NotStarted;
    }

    public void MarkPaymentAuthorized(string payPalOrderId, string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = payPalOrderId;
        PayPalAuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        PaymentStatus = PaymentStatus.Authorized;
        Status = OrderStatus.PaymentAuthorized;
    }

    public void MarkAuthorizationRenewed(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalAuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        PaymentStatus = PaymentStatus.Authorized;
    }

    /// <summary>
    /// The held funds can no longer be captured and the hold cannot be renewed. The order
    /// goes back to awaiting payment so the shopper can pay again; no money has moved.
    /// </summary>
    public void MarkAuthorizationUnrecoverable()
    {
        PaymentStatus = PaymentStatus.AuthorizationUnrecoverable;
        Status = OrderStatus.AwaitingPayment;
    }

    public void MarkCaptured(string captureId, decimal grossAmount, decimal? payPalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));

        PayPalCaptureId = captureId;
        CapturedAmount = grossAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        PaymentStatus = PaymentStatus.Captured;
        Status = OrderStatus.Fulfilled;
    }

    public void MarkVoided()
    {
        PaymentStatus = PaymentStatus.Voided;
        Status = OrderStatus.Cancelled;
    }

    public void MarkCancelledWithoutPayment()
    {
        Status = OrderStatus.Cancelled;
    }

    public void ApplyRefund(PaymentRefund refund)
    {
        Guard.Against.Null(refund, nameof(refund));
        if (refund.Amount > RefundableAmount())
        {
            throw new InvalidOperationException(
                $"Refund of {refund.Amount} exceeds the remaining refundable amount of {RefundableAmount()} on order {Id}.");
        }

        _paymentRefunds.Add(refund);
        RefundedAmount += refund.Amount;
        PaymentStatus = RefundedAmount >= (CapturedAmount ?? 0m)
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;
    }
}
