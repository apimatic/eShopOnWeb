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
        : this(buyerId, shipToAddress, items, null)
    {
    }

    public Order(string buyerId, Address shipToAddress, List<OrderItem> items, string? paymentCurrency)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        BuyerId = buyerId;
        ShipToAddress = shipToAddress;
        _orderItems = items;
        Currency = paymentCurrency;
        OrderStatus = paymentCurrency is null ? OrderLifecycleStatus.Fulfilled : OrderLifecycleStatus.AwaitingPayment;
        PaymentStatus = paymentCurrency is null ? PaymentLifecycleStatus.None : PaymentLifecycleStatus.AwaitingPayment;
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public OrderLifecycleStatus OrderStatus { get; private set; }
    public PaymentLifecycleStatus PaymentStatus { get; private set; }
    public string? Currency { get; private set; }
    public string? PayPalOrderId { get; private set; }
    public string? PayPalOrderStatus { get; private set; }
    public string? PayPalAuthorizationId { get; private set; }
    public string? PayPalAuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationCreatedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? PayPalCaptureId { get; private set; }
    public string? PayPalCaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetProceeds { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public string? CreateOrderRequestId { get; private set; }
    public string? AuthorizeRequestId { get; private set; }
    public string? ReauthorizeRequestId { get; private set; }
    public string? CaptureRequestId { get; private set; }
    public string? VoidRequestId { get; private set; }
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

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

    private readonly List<PaymentRefund> _refunds = new();
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();

    public decimal RefundedAmount()
    {
        decimal total = 0;
        foreach (var refund in _refunds)
        {
            if (refund.IsCompleted)
            {
                total += refund.Amount;
            }
        }
        return total;
    }

    public decimal ReservedRefundAmount()
    {
        decimal total = 0;
        foreach (var refund in _refunds)
        {
            if (refund.ReservesFunds)
            {
                total += refund.Amount;
            }
        }
        return total;
    }

    public void EnsurePaymentRequestIds()
    {
        CreateOrderRequestId ??= Guid.NewGuid().ToString("N");
        AuthorizeRequestId ??= Guid.NewGuid().ToString("N");
        PaymentStatus = PaymentLifecycleStatus.Authorizing;
    }

    public void RecordPayPalOrder(string orderId, string status)
    {
        PayPalOrderId = orderId;
        PayPalOrderStatus = status;
    }

    public void RecordAuthorization(string authorizationId, string status, DateTimeOffset? createdAt, DateTimeOffset? expiresAt)
    {
        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = status;
        AuthorizationCreatedAt = createdAt ?? DateTimeOffset.UtcNow;
        AuthorizationExpiresAt = expiresAt;
        PaymentStatus = PaymentLifecycleStatus.Authorized;
        OrderStatus = OrderLifecycleStatus.Authorized;
    }

    public void EnsureCaptureRequestId() => CaptureRequestId ??= Guid.NewGuid().ToString("N");

    public void EnsureReauthorizeRequestId() => ReauthorizeRequestId ??= Guid.NewGuid().ToString("N");

    public void RecordCapturePending(string captureId, string status)
    {
        PayPalCaptureId = captureId;
        PayPalCaptureStatus = status;
        PaymentStatus = PaymentLifecycleStatus.CapturePending;
    }

    public void RecordCapture(string captureId, string status, decimal gross, decimal? fee, decimal? net, DateTimeOffset? capturedAt)
    {
        PayPalCaptureId = captureId;
        PayPalCaptureStatus = status;
        CapturedAmount = gross;
        PayPalFee = fee;
        NetProceeds = net;
        CapturedAt = capturedAt ?? DateTimeOffset.UtcNow;
        PaymentStatus = PaymentLifecycleStatus.Captured;
        OrderStatus = OrderLifecycleStatus.Fulfilled;
    }

    public void EnsureVoidRequestId() => VoidRequestId ??= Guid.NewGuid().ToString("N");

    public void RecordVoided(string status)
    {
        PayPalAuthorizationStatus = status;
        PaymentStatus = PaymentLifecycleStatus.Voided;
        OrderStatus = OrderLifecycleStatus.Cancelled;
    }

    public PaymentRefund ReserveRefund(string idempotencyKey, string providerRequestId, decimal amount)
    {
        if (Currency is null) throw new InvalidOperationException("Order has no payment currency.");
        var refund = new PaymentRefund(idempotencyKey, providerRequestId, amount, Currency);
        _refunds.Add(refund);
        return refund;
    }

    public void RecalculateRefundState()
    {
        var captured = CapturedAmount ?? 0;
        var refunded = RefundedAmount();
        if (refunded <= 0) return;
        if (refunded >= captured)
        {
            PaymentStatus = PaymentLifecycleStatus.Refunded;
            OrderStatus = OrderLifecycleStatus.Refunded;
        }
        else
        {
            PaymentStatus = PaymentLifecycleStatus.PartiallyRefunded;
            OrderStatus = OrderLifecycleStatus.PartiallyRefunded;
        }
    }

    public void RecordPaymentFailure() => PaymentStatus = PaymentLifecycleStatus.Failed;

    public decimal Total()
    {
        var total = 0m;
        foreach (var item in _orderItems)
        {
            total += item.UnitPrice * item.Units;
        }
        return total;
    }
}
