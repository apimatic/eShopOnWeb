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
        PaymentStatus = OrderPaymentStatus.AwaitingPayment;
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }

    public OrderPaymentStatus PaymentStatus { get; private set; } = OrderPaymentStatus.AwaitingPayment;
    public string? Currency { get; private set; }

    public string? PayPalOrderId { get; private set; }
    public string? PayPalOrderStatus { get; private set; }

    public string? PayPalAuthorizationId { get; private set; }
    public string? PayPalAuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationCreatedAt { get; private set; }
    public DateTimeOffset? OriginalAuthorizationCreatedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiration { get; private set; }
    public decimal? AuthorizedAmount { get; private set; }

    public string? PayPalCaptureId { get; private set; }
    public string? PayPalCaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    public string? PayRequestId { get; private set; }
    public string? FulfilRequestId { get; private set; }
    public string? CancelRequestId { get; private set; }

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

    public decimal RefundedTotal() => _refunds.Sum(r => r.Amount);

    public decimal RemainingRefundable()
    {
        var captured = CapturedAmount ?? 0m;
        var remaining = captured - RefundedTotal();
        return remaining < 0 ? 0 : remaining;
    }

    public OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);

    public string EnsurePayRequestId()
    {
        PayRequestId ??= $"pay-order-{Id}-{Guid.NewGuid():N}";
        return PayRequestId;
    }

    public void RotatePayRequestId()
    {
        PayRequestId = $"pay-order-{Id}-{Guid.NewGuid():N}";
    }

    public string EnsureFulfilRequestId()
    {
        FulfilRequestId ??= $"fulfil-order-{Id}-{Guid.NewGuid():N}";
        return FulfilRequestId;
    }

    public string EnsureCancelRequestId()
    {
        CancelRequestId ??= $"cancel-order-{Id}-{Guid.NewGuid():N}";
        return CancelRequestId;
    }

    public void RecordPayPalOrder(string paypalOrderId, string? status)
    {
        Guard.Against.NullOrEmpty(paypalOrderId, nameof(paypalOrderId));
        PayPalOrderId = paypalOrderId;
        PayPalOrderStatus = status;
    }

    public void MarkAuthorized(
        string paypalOrderId,
        string paypalOrderStatus,
        string authorizationId,
        string authorizationStatus,
        decimal authorizedAmount,
        string currency,
        DateTimeOffset? createdAt,
        DateTimeOffset? expiration)
    {
        EnsureCanAuthorize();

        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PayPalOrderId = paypalOrderId;
        PayPalOrderStatus = paypalOrderStatus;
        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = authorizationStatus;
        AuthorizedAmount = authorizedAmount;
        Currency = currency;
        AuthorizationCreatedAt = createdAt ?? DateTimeOffset.UtcNow;
        OriginalAuthorizationCreatedAt ??= AuthorizationCreatedAt;
        AuthorizationExpiration = expiration;
        PaymentStatus = OrderPaymentStatus.Authorized;
    }

    public void SyncAuthorizationStatus(string authorizationStatus)
    {
        if (!string.IsNullOrWhiteSpace(authorizationStatus))
        {
            PayPalAuthorizationStatus = authorizationStatus;
        }
    }

    public void ReplaceAuthorization(
        string authorizationId,
        string authorizationStatus,
        DateTimeOffset? createdAt,
        DateTimeOffset? expiration)
    {
        if (PaymentStatus != OrderPaymentStatus.Authorized)
        {
            throw new CheckoutException(409, $"Order {Id} is {PaymentStatus} and cannot have its authorization renewed.");
        }

        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = authorizationStatus;
        AuthorizationCreatedAt = createdAt ?? DateTimeOffset.UtcNow;
        AuthorizationExpiration = expiration;
    }

    public void MarkFulfilled(
        string captureId,
        string captureStatus,
        decimal capturedAmount,
        decimal? paypalFee,
        decimal? netAmount)
    {
        if (PaymentStatus is OrderPaymentStatus.Fulfilled or OrderPaymentStatus.Refunded or OrderPaymentStatus.PartiallyRefunded)
        {
            return;
        }

        if (PaymentStatus != OrderPaymentStatus.Authorized)
        {
            throw new CheckoutException(409, $"Order {Id} is {PaymentStatus} and cannot be fulfilled.");
        }

        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        PayPalCaptureId = captureId;
        PayPalCaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = paypalFee;
        NetAmount = netAmount;
        PayPalAuthorizationStatus = "CAPTURED";
        PaymentStatus = OrderPaymentStatus.Fulfilled;
    }

    public void MarkCancelled()
    {
        if (PaymentStatus == OrderPaymentStatus.Cancelled)
        {
            return;
        }

        if (PaymentStatus is OrderPaymentStatus.Fulfilled or OrderPaymentStatus.Refunded or OrderPaymentStatus.PartiallyRefunded)
        {
            throw new CheckoutException(409, $"Order {Id} has already been fulfilled. Cancel is only allowed before fulfilment; refund the capture instead.");
        }

        PayPalAuthorizationStatus = PaymentStatus == OrderPaymentStatus.Authorized ? "VOIDED" : PayPalAuthorizationStatus;
        PaymentStatus = OrderPaymentStatus.Cancelled;
    }

    public OrderRefund AddRefund(string paypalRefundId, string status, decimal amount, string currency, string idempotencyKey)
    {
        if (PaymentStatus is not (OrderPaymentStatus.Fulfilled or OrderPaymentStatus.PartiallyRefunded or OrderPaymentStatus.Refunded))
        {
            throw new CheckoutException(409, $"Order {Id} is {PaymentStatus} and cannot be refunded. Refunds are only allowed after fulfilment.");
        }

        Guard.Against.NullOrEmpty(paypalRefundId, nameof(paypalRefundId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NegativeOrZero(amount, nameof(amount));

        var remaining = RemainingRefundable();
        if (amount > remaining)
        {
            throw new CheckoutException(409, $"Refund of {amount:0.00} exceeds the remaining refundable amount {remaining:0.00} of the captured {CapturedAmount:0.00}.");
        }

        var refund = new OrderRefund(paypalRefundId, status, amount, currency, idempotencyKey);
        _refunds.Add(refund);

        PaymentStatus = RemainingRefundable() == 0m
            ? OrderPaymentStatus.Refunded
            : OrderPaymentStatus.PartiallyRefunded;
        PayPalCaptureStatus = PaymentStatus == OrderPaymentStatus.Refunded ? "REFUNDED" : "PARTIALLY_REFUNDED";

        return refund;
    }

    public bool BelongsTo(string buyerId) =>
        string.Equals(BuyerId, buyerId, StringComparison.Ordinal);

    private void EnsureCanAuthorize()
    {
        if (PaymentStatus == OrderPaymentStatus.Authorized)
        {
            return;
        }

        if (PaymentStatus != OrderPaymentStatus.AwaitingPayment)
        {
            throw new CheckoutException(409, $"Order {Id} is {PaymentStatus} and cannot be authorized.");
        }
    }
}
