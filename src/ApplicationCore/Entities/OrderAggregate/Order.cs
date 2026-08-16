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
    private Order() { }

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
    /// A stable, globally-unique reference for this order used to derive PayPal
    /// idempotency keys (PayPal-Request-Id). Being per-order-instance and random, it
    /// keeps retries within a run idempotent while never colliding with a different
    /// order — even after the in-memory database resets and reuses low integer ids.
    /// </summary>
    public string PaymentReference { get; private set; } = Guid.NewGuid().ToString("N");
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
    // Payment state (additive — see OrderPaymentStatus). All PayPal-owned ids
    // and statuses live here so a later request can act on the payment, not just
    // the request that created it.
    // ---------------------------------------------------------------------

    public OrderPaymentStatus PaymentStatus { get; private set; } = OrderPaymentStatus.AwaitingPayment;

    /// <summary>ISO-4217 currency the payment was authorized in (from configuration).</summary>
    public string? PaymentCurrency { get; private set; }

    /// <summary>Human-safe description of the instrument used, e.g. "VISA ****1111". Never full card details.</summary>
    public string? PaymentMethodDescription { get; private set; }

    /// <summary>PayPal Orders API order id backing the hold.</summary>
    public string? PayPalOrderId { get; private set; }

    /// <summary>PayPal authorization id (the hold).</summary>
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    /// <summary>PayPal capture id (set at fulfilment).</summary>
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }

    /// <summary>Gross amount PayPal captured.</summary>
    public decimal? CapturedAmount { get; private set; }

    /// <summary>PayPal's fee on the capture.</summary>
    public decimal? PayPalFee { get; private set; }

    /// <summary>Net proceeds to the merchant after PayPal's fee.</summary>
    public decimal? NetAmount { get; private set; }

    private readonly List<OrderRefund> _refunds = new List<OrderRefund>();
    public IReadOnlyCollection<OrderRefund> Refunds => _refunds.AsReadOnly();

    /// <summary>Sum of all refunds recorded against the capture.</summary>
    public decimal RefundedAmount => _refunds.Sum(r => r.Amount);

    /// <summary>How much of the captured payment can still be refunded.</summary>
    public decimal RefundableAmount => (CapturedAmount ?? 0m) - RefundedAmount;

    /// <summary>Record the money hold created at PayPal.</summary>
    public void MarkAuthorized(string payPalOrderId, string authorizationId, string authorizationStatus,
        DateTimeOffset? authorizationExpiresAt, string currency, string paymentMethodDescription)
    {
        if (PaymentStatus != OrderPaymentStatus.AwaitingPayment)
        {
            throw new OrderPaymentException($"Order {Id} cannot be authorized from status {PaymentStatus}.");
        }

        PayPalOrderId = Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        AuthorizationId = Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = authorizationExpiresAt;
        PaymentCurrency = Guard.Against.NullOrEmpty(currency, nameof(currency));
        PaymentMethodDescription = paymentMethodDescription;
        PaymentStatus = OrderPaymentStatus.Authorized;
    }

    /// <summary>Replace a stale authorization with a freshly reauthorized one before capture.</summary>
    public void RenewAuthorization(string newAuthorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        if (PaymentStatus != OrderPaymentStatus.Authorized)
        {
            throw new OrderPaymentException($"Order {Id} authorization cannot be renewed from status {PaymentStatus}.");
        }

        AuthorizationId = Guard.Against.NullOrEmpty(newAuthorizationId, nameof(newAuthorizationId));
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    /// <summary>Record the capture that took the money at fulfilment.</summary>
    public void MarkCaptured(string captureId, string captureStatus, decimal grossAmount, decimal payPalFee, decimal netAmount)
    {
        if (PaymentStatus != OrderPaymentStatus.Authorized)
        {
            throw new OrderPaymentException($"Order {Id} cannot be captured from status {PaymentStatus}.");
        }

        CaptureId = Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        CaptureStatus = captureStatus;
        CapturedAmount = grossAmount;
        PayPalFee = payPalFee;
        NetAmount = netAmount;
        PaymentStatus = OrderPaymentStatus.Captured;
    }

    /// <summary>Record that the held funds were released before fulfilment.</summary>
    public void MarkCancelled()
    {
        if (PaymentStatus != OrderPaymentStatus.Authorized)
        {
            throw new OrderPaymentException($"Order {Id} cannot be cancelled from status {PaymentStatus}.");
        }

        AuthorizationStatus = "VOIDED";
        PaymentStatus = OrderPaymentStatus.Cancelled;
    }

    /// <summary>
    /// Record a refund against the capture. Guards that total refunds never exceed the captured amount.
    /// </summary>
    public void AddRefund(OrderRefund refund)
    {
        Guard.Against.Null(refund, nameof(refund));
        if (PaymentStatus != OrderPaymentStatus.Captured && PaymentStatus != OrderPaymentStatus.PartiallyRefunded)
        {
            throw new OrderPaymentException($"Order {Id} cannot be refunded from status {PaymentStatus}.");
        }

        if (refund.Amount > RefundableAmount)
        {
            throw new OrderPaymentException(
                $"Refund of {refund.Amount} exceeds the refundable balance of {RefundableAmount} for order {Id}.");
        }

        _refunds.Add(refund);
        PaymentStatus = RefundedAmount >= (CapturedAmount ?? 0m)
            ? OrderPaymentStatus.Refunded
            : OrderPaymentStatus.PartiallyRefunded;
    }

    /// <summary>Returns an existing refund recorded under the given idempotency key, if any.</summary>
    public OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
}
