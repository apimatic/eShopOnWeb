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

    public Order(string buyerId, Address shipToAddress, List<OrderItem> items, string currency = "USD")
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        BuyerId = buyerId;
        ShipToAddress = shipToAddress;
        _orderItems = items;
        Currency = currency;
        PaymentStatus = OrderPaymentStatus.AwaitingPayment;
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

    public OrderPaymentStatus PaymentStatus { get; private set; } = OrderPaymentStatus.AwaitingPayment;
    public string Currency { get; private set; } = "USD";

    public string? PayPalOrderId { get; private set; }
    public string? PayPalAuthorizationId { get; private set; }
    public string? PayPalAuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationCreatedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    public string? PayPalCaptureId { get; private set; }
    public string? PayPalCaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    public DateTimeOffset? AuthorizedAt { get; private set; }
    public DateTimeOffset? FulfilledAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public string? PaymentAttemptKey { get; private set; }

    public string PayPalInvoiceId => string.IsNullOrEmpty(PaymentAttemptKey) ? $"ESHOP-{Id}" : $"ESHOP-{Id}-{PaymentAttemptKey}";

    public string EnsurePaymentAttemptKey()
    {
        PaymentAttemptKey ??= Guid.NewGuid().ToString("N");
        return PaymentAttemptKey;
    }

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal RefundedAmount => _refunds.Sum(r => r.Amount);

    public decimal RemainingRefundableAmount
    {
        get
        {
            var captured = CapturedAmount ?? 0m;
            var remaining = captured - RefundedAmount;
            return remaining < 0 ? 0 : remaining;
        }
    }

    public void AttachPayPalOrder(string payPalOrderId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        PayPalOrderId ??= payPalOrderId;
    }

    public void RecordAuthorization(
        string payPalOrderId,
        string authorizationId,
        string authorizationStatus,
        DateTimeOffset? createdAt,
        DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(authorizationStatus, nameof(authorizationStatus));

        if (PaymentStatus == OrderPaymentStatus.Cancelled)
        {
            throw new PaymentException(409, "This order was cancelled and cannot be paid.");
        }

        if (PaymentStatus is OrderPaymentStatus.Fulfilled or OrderPaymentStatus.Refunded or OrderPaymentStatus.PartiallyRefunded)
        {
            throw new PaymentException(409, "This order has already been fulfilled and cannot be authorized again.");
        }

        PayPalOrderId = payPalOrderId;
        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = authorizationStatus;
        AuthorizationCreatedAt = createdAt;
        AuthorizationExpiresAt = expiresAt;
        AuthorizedAt = DateTimeOffset.UtcNow;
        PaymentStatus = OrderPaymentStatus.Authorized;
    }

    public void SyncAuthorizationStatus(string status)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        PayPalAuthorizationStatus = status;
    }

    public void RecordReauthorization(string authorizationId, string authorizationStatus, DateTimeOffset? createdAt, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(authorizationStatus, nameof(authorizationStatus));

        if (PaymentStatus != OrderPaymentStatus.Authorized)
        {
            throw new PaymentException(409, "Only an authorized order can have its hold renewed.");
        }

        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = authorizationStatus;
        AuthorizationCreatedAt = createdAt;
        AuthorizationExpiresAt = expiresAt;
    }

    public void RecordCapture(
        string captureId,
        string captureStatus,
        decimal capturedAmount,
        decimal? paypalFee,
        decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        Guard.Against.NullOrEmpty(captureStatus, nameof(captureStatus));

        if (PaymentStatus is OrderPaymentStatus.Fulfilled or OrderPaymentStatus.Refunded or OrderPaymentStatus.PartiallyRefunded)
        {
            return;
        }

        if (PaymentStatus != OrderPaymentStatus.Authorized)
        {
            throw new PaymentException(409, "An order must be authorized before it can be fulfilled.");
        }

        PayPalCaptureId = captureId;
        PayPalCaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = paypalFee;
        NetAmount = netAmount;
        PayPalAuthorizationStatus = "CAPTURED";
        FulfilledAt = DateTimeOffset.UtcNow;
        PaymentStatus = OrderPaymentStatus.Fulfilled;
    }

    public void RecordCancellation(string? authorizationStatus = "VOIDED")
    {
        if (PaymentStatus == OrderPaymentStatus.Cancelled)
        {
            return;
        }

        if (PaymentStatus is OrderPaymentStatus.Fulfilled or OrderPaymentStatus.Refunded or OrderPaymentStatus.PartiallyRefunded)
        {
            throw new PaymentException(409, "A fulfilled order cannot be cancelled. Issue a refund instead.");
        }

        if (authorizationStatus != null)
        {
            PayPalAuthorizationStatus = authorizationStatus;
        }

        CancelledAt = DateTimeOffset.UtcNow;
        PaymentStatus = OrderPaymentStatus.Cancelled;
    }

    public PaymentRefund RecordRefund(string payPalRefundId, string status, decimal amount, string currency, string idempotencyKey)
    {
        if (PaymentStatus is not (OrderPaymentStatus.Fulfilled or OrderPaymentStatus.PartiallyRefunded))
        {
            throw new PaymentException(409, "Refunds can only be issued after the order has been fulfilled.");
        }

        if (amount > RemainingRefundableAmount)
        {
            throw new PaymentException(409,
                $"Cannot refund {amount} {currency}. Only {RemainingRefundableAmount} {currency} remains refundable on this capture.");
        }

        var refund = new PaymentRefund(payPalRefundId, status, amount, currency, idempotencyKey);
        _refunds.Add(refund);

        PaymentStatus = RemainingRefundableAmount == 0m
            ? OrderPaymentStatus.Refunded
            : OrderPaymentStatus.PartiallyRefunded;

        return refund;
    }

    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey)
    {
        return _refunds.FirstOrDefault(r =>
            string.Equals(r.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));
    }
}
