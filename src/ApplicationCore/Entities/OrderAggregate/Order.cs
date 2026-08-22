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
        PaymentStatus = OrderPaymentStatus.PendingPayment;
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public OrderPaymentStatus PaymentStatus { get; private set; } = OrderPaymentStatus.PendingPayment;
    public string? PayPalOrderId { get; private set; }
    public string? PayPalAuthorizationId { get; private set; }
    public string? PayPalAuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public DateTimeOffset? OriginalAuthorizationCreatedAt { get; private set; }
    public string? PayPalCaptureId { get; private set; }
    public string? PayPalCaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PaypalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public string? PaymentCurrency { get; private set; }
    public string? PaymentIdempotencyKey { get; private set; }

    // DDD Patterns comment
    // Using a private collection field, better for DDD Aggregate's encapsulation
    // so OrderItems cannot be added from "outside the AggregateRoot" directly to the collection,
    // but only through the method Order.AddOrderItem() which includes behavior.
    private readonly List<OrderItem> _orderItems = new List<OrderItem>();
    private readonly List<OrderRefund> _refunds = new List<OrderRefund>();

    // Using List<>.AsReadOnly() 
    // This will create a read only wrapper around the private list so is protected against "external updates".
    // It's much cheaper than .ToList() because it will not have to copy all items in a new collection. (Just one heap alloc for the wrapper instance)
    //https://msdn.microsoft.com/en-us/library/e78dcd75(v=vs.110).aspx
    public IReadOnlyCollection<OrderItem> OrderItems => _orderItems.AsReadOnly();
    public IReadOnlyCollection<OrderRefund> Refunds => _refunds.AsReadOnly();

    public decimal Total()
    {
        var total = 0m;
        foreach (var item in _orderItems)
        {
            total += item.UnitPrice * item.Units;
        }
        return total;
    }

    public decimal RemainingRefundable()
    {
        if (CapturedAmount is null)
        {
            return 0m;
        }

        var refunded = _refunds.Where(r => r.CountsAgainstCapturedAmount).Sum(r => r.Amount);
        var remaining = CapturedAmount.Value - refunded;
        return remaining < 0m ? 0m : remaining;
    }

    public OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => string.Equals(r.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));

    public void RecordAuthorization(
        string payPalOrderId,
        string authorizationId,
        string? authorizationStatus,
        DateTimeOffset? expiresAt,
        DateTimeOffset? createdAt,
        string currency)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        if (PaymentStatus is OrderPaymentStatus.Fulfilled or OrderPaymentStatus.Cancelled
            or OrderPaymentStatus.Refunded or OrderPaymentStatus.PartiallyRefunded)
        {
            throw new CheckoutException($"Order {Id} cannot be authorized in status {PaymentStatus}.", 409);
        }

        PayPalOrderId = payPalOrderId;
        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        OriginalAuthorizationCreatedAt ??= createdAt;
        PaymentCurrency = currency;
        PaymentStatus = OrderPaymentStatus.Authorized;
    }

    public string EnsurePaymentIdempotencyKey()
    {
        PaymentIdempotencyKey ??= Guid.NewGuid().ToString("N");
        return PaymentIdempotencyKey;
    }

    public void ReplaceAuthorization(string authorizationId, string? authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    public void RecordCapture(
        string captureId,
        string? captureStatus,
        decimal capturedAmount,
        decimal? paypalFee,
        decimal? netAmount,
        string currency)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));

        PayPalCaptureId = captureId;
        PayPalCaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PaypalFee = paypalFee;
        NetAmount = netAmount;
        PaymentCurrency = currency;
        PaymentStatus = OrderPaymentStatus.Fulfilled;
    }

    public void RecordVoid(string? authorizationStatus)
    {
        if (PaymentStatus == OrderPaymentStatus.Cancelled)
        {
            return;
        }

        if (PaymentStatus is OrderPaymentStatus.Fulfilled or OrderPaymentStatus.Refunded or OrderPaymentStatus.PartiallyRefunded)
        {
            throw new CheckoutException($"Order {Id} cannot be cancelled after fulfilment.", 409);
        }

        PayPalAuthorizationStatus = authorizationStatus ?? "VOIDED";
        PaymentStatus = OrderPaymentStatus.Cancelled;
    }

    public OrderRefund RecordRefund(string payPalRefundId, string idempotencyKey, decimal amount, string status)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        var refund = new OrderRefund(payPalRefundId, idempotencyKey, amount, status);
        _refunds.Add(refund);

        var remaining = RemainingRefundable();
        PaymentStatus = remaining <= 0m
            ? OrderPaymentStatus.Refunded
            : OrderPaymentStatus.PartiallyRefunded;

        if (!string.IsNullOrEmpty(PayPalCaptureStatus))
        {
            PayPalCaptureStatus = PaymentStatus == OrderPaymentStatus.Refunded ? "REFUNDED" : "PARTIALLY_REFUNDED";
        }

        return refund;
    }
}
