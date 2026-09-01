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

    // ---- Payment state (PayPal owns the remote side; these are the ids/statuses needed to act later) ----

    public OrderPaymentStatus PaymentStatus { get; private set; } = OrderPaymentStatus.AwaitingPayment;

    /// <summary>ISO-4217 currency this order's payment operations use.</summary>
    public string? Currency { get; private set; }

    /// <summary>PayPal order id created when payment started.</summary>
    public string? PayPalOrderId { get; private set; }

    /// <summary>PayPal authorization id currently holding the funds.</summary>
    public string? AuthorizationId { get; private set; }

    /// <summary>PayPal's status wire value for the current authorization.</summary>
    public string? AuthorizationStatus { get; private set; }

    public DateTimeOffset? AuthorizedAt { get; private set; }

    /// <summary>PayPal's expiration_time for the current authorization hold.</summary>
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }

    public string? CaptureId { get; private set; }
    public decimal? CapturedGrossAmount { get; private set; }

    /// <summary>PayPal's processing fee as reported on the capture.</summary>
    public decimal? CapturedFeeAmount { get; private set; }

    /// <summary>Net proceeds to the merchant as reported on the capture.</summary>
    public decimal? CapturedNetAmount { get; private set; }

    /// <summary>Monotonic counters so every attempt at a PayPal write gets a fresh idempotency key.</summary>
    public int PaymentAttemptCount { get; private set; }
    public int CaptureAttemptCount { get; private set; }
    public int VoidAttemptCount { get; private set; }

    private readonly List<OrderRefund> _refunds = new List<OrderRefund>();
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

    public decimal TotalRefunded => _refunds.Where(r => r.ConsumesRefundableAmount).Sum(r => r.Amount);

    public decimal RemainingRefundable => (CapturedGrossAmount ?? 0m) - TotalRefunded;

    public void SetCurrency(string currency)
    {
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Currency ??= currency;
    }

    public int NextPaymentAttempt() => ++PaymentAttemptCount;
    public int NextCaptureAttempt() => ++CaptureAttemptCount;
    public int NextVoidAttempt() => ++VoidAttemptCount;

    public void RegisterPayPalOrder(string payPalOrderId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        PayPalOrderId = payPalOrderId;
    }

    public void MarkAuthorized(string authorizationId, string status, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        if (PaymentStatus != OrderPaymentStatus.AwaitingPayment)
        {
            throw new PaymentStateException($"Order {Id} is {PaymentStatus} and cannot be authorized.");
        }

        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
        AuthorizedAt = DateTimeOffset.UtcNow;
        PaymentStatus = OrderPaymentStatus.Authorized;
    }

    /// <summary>Records a renewed (re-authorized) hold; PayPal may return a new authorization id.</summary>
    public void MarkReauthorized(string authorizationId, string status, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        if (PaymentStatus != OrderPaymentStatus.Authorized)
        {
            throw new PaymentStateException($"Order {Id} is {PaymentStatus} and has no authorization to renew.");
        }

        AuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt;
        AuthorizedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Refreshes the stored authorization status from PayPal without changing lifecycle state.</summary>
    public void SyncAuthorizationStatus(string status, DateTimeOffset? expiresAt)
    {
        AuthorizationStatus = status;
        AuthorizationExpiresAt = expiresAt ?? AuthorizationExpiresAt;
    }

    public void MarkCaptured(string captureId, decimal gross, decimal? fee, decimal? net)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        if (PaymentStatus != OrderPaymentStatus.Authorized)
        {
            throw new PaymentStateException($"Order {Id} is {PaymentStatus} and cannot be captured.");
        }

        CaptureId = captureId;
        CapturedGrossAmount = gross;
        CapturedFeeAmount = fee;
        CapturedNetAmount = net;
        PaymentStatus = OrderPaymentStatus.Captured;
    }

    public void MarkCapturePending(string captureId, decimal? gross, decimal? fee, decimal? net)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        if (PaymentStatus != OrderPaymentStatus.Authorized)
        {
            throw new PaymentStateException($"Order {Id} is {PaymentStatus} and cannot be captured.");
        }

        CaptureId = captureId;
        CapturedGrossAmount = gross;
        CapturedFeeAmount = fee;
        CapturedNetAmount = net;
        PaymentStatus = OrderPaymentStatus.CapturePending;
    }

    public void MarkCancelled()
    {
        if (PaymentStatus is OrderPaymentStatus.Captured or OrderPaymentStatus.CapturePending
            or OrderPaymentStatus.PartiallyRefunded or OrderPaymentStatus.Refunded)
        {
            throw new PaymentStateException($"Order {Id} has captured funds and cannot be cancelled; refund it instead.");
        }

        PaymentStatus = OrderPaymentStatus.Cancelled;
    }

    public OrderRefund AddRefund(string payPalRefundId, decimal amount, string currency, string status, string idempotencyKey)
    {
        if (PaymentStatus is not (OrderPaymentStatus.Captured or OrderPaymentStatus.PartiallyRefunded))
        {
            throw new PaymentStateException($"Order {Id} is {PaymentStatus} and has no captured payment to refund.");
        }

        var refund = new OrderRefund(payPalRefundId, amount, currency, status, idempotencyKey);
        _refunds.Add(refund);

        if (refund.ConsumesRefundableAmount && RemainingRefundable <= 0m)
        {
            PaymentStatus = OrderPaymentStatus.Refunded;
        }
        else if (refund.ConsumesRefundableAmount)
        {
            PaymentStatus = OrderPaymentStatus.PartiallyRefunded;
        }

        return refund;
    }
}
