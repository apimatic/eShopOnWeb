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
        : this(buyerId, shipToAddress, items, null)
    {
    }

    public Order(string buyerId, Address shipToAddress, List<OrderItem> items, string? currency)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        BuyerId = buyerId;
        ShipToAddress = shipToAddress;
        _orderItems = items;
        OrderTotal = Total();
        Currency = currency ?? string.Empty;
        PaymentStatus = currency is null ? OrderPaymentStatus.NotRequired : OrderPaymentStatus.AwaitingPayment;
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public decimal OrderTotal { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public OrderPaymentStatus PaymentStatus { get; private set; }
    public DateTimeOffset? PaymentUpdatedAt { get; private set; }
    public DateTimeOffset? FulfilledAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public string? PayPalOrderId { get; private set; }
    public string? PayPalOrderStatus { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public decimal? AuthorizedAmount { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public DateTimeOffset? AuthorizationCreatedAt { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetProceeds { get; private set; }
    public string? PaymentSource { get; private set; }
    public string? PayPalCreateRequestId { get; private set; }
    public string? PayPalAuthorizeRequestId { get; private set; }
    public string? PayPalCaptureRequestId { get; private set; }
    public string? PayPalVoidRequestId { get; private set; }
    public string? PayPalReauthorizeRequestId { get; private set; }
    public int ReauthorizationCount { get; private set; }
    public string PaymentCorrelationId { get; private set; } = Guid.NewGuid().ToString("N");
    public string ConcurrencyToken { get; private set; } = Guid.NewGuid().ToString("N");

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

    public decimal RefundedAmount() => _refunds
        .Where(x => x.Status is not PaymentRefundStatus.Failed and not PaymentRefundStatus.Cancelled)
        .Sum(x => x.Amount);

    public void ReservePayment(string createRequestId, string authorizeRequestId, string source)
    {
        if (PaymentStatus is not OrderPaymentStatus.AwaitingPayment and not OrderPaymentStatus.PaymentFailed
            and not OrderPaymentStatus.Authorizing)
            throw new InvalidOperationException("This order is not awaiting payment.");
        if (PaymentStatus == OrderPaymentStatus.Authorizing &&
            (PayPalCreateRequestId != createRequestId || PayPalAuthorizeRequestId != authorizeRequestId))
            throw new InvalidOperationException("A different payment attempt is already in progress.");
        PayPalCreateRequestId ??= createRequestId;
        PayPalAuthorizeRequestId ??= authorizeRequestId;
        PaymentSource = source;
        PaymentStatus = OrderPaymentStatus.Authorizing;
        Touch();
    }

    public string EnsurePaymentCorrelationId()
    {
        if (string.IsNullOrWhiteSpace(PaymentCorrelationId))
        {
            PaymentCorrelationId = Guid.NewGuid().ToString("N");
            Touch();
        }
        return PaymentCorrelationId;
    }

    public void RecordPayPalOrder(string id, string? status)
    {
        PayPalOrderId = id;
        PayPalOrderStatus = status;
        Touch();
    }

    public void RecordAuthorization(string id, string? status, decimal amount,
        DateTimeOffset? expiresAt, DateTimeOffset? createdAt)
    {
        AuthorizationId = id;
        AuthorizationStatus = status;
        AuthorizedAmount = amount;
        AuthorizationExpiresAt = expiresAt;
        AuthorizationCreatedAt ??= createdAt ?? DateTimeOffset.UtcNow;
        PayPalReauthorizeRequestId = null;
        PaymentStatus = OrderPaymentStatus.Authorized;
        Touch();
    }

    public void MarkPaymentFailed(string? providerStatus)
    {
        AuthorizationStatus = providerStatus;
        PaymentStatus = OrderPaymentStatus.PaymentFailed;
        Touch();
    }

    public string ReserveReauthorization()
    {
        if (PayPalReauthorizeRequestId is null)
        {
            ReauthorizationCount++;
            PayPalReauthorizeRequestId = $"eshop-order-{EnsurePaymentCorrelationId()}-reauthorize-{ReauthorizationCount}";
            Touch();
        }
        return PayPalReauthorizeRequestId;
    }

    public string ReserveCapture()
    {
        PayPalCaptureRequestId ??= $"eshop-order-{EnsurePaymentCorrelationId()}-capture";
        PaymentStatus = OrderPaymentStatus.CapturePending;
        Touch();
        return PayPalCaptureRequestId;
    }

    public void RecordCapture(string id, string? status, decimal amount, decimal? fee, decimal? net)
    {
        CaptureId = id;
        CaptureStatus = status;
        CapturedAmount = amount;
        PayPalFee = fee;
        NetProceeds = net;
        PaymentStatus = string.Equals(status, "COMPLETED", StringComparison.OrdinalIgnoreCase)
            ? OrderPaymentStatus.Captured : OrderPaymentStatus.CapturePending;
        if (PaymentStatus == OrderPaymentStatus.Captured) FulfilledAt = DateTimeOffset.UtcNow;
        Touch();
    }

    public string ReserveVoid()
    {
        PayPalVoidRequestId ??= $"eshop-order-{EnsurePaymentCorrelationId()}-void";
        Touch();
        return PayPalVoidRequestId;
    }

    public void MarkCancelled(string? authorizationStatus)
    {
        AuthorizationStatus = authorizationStatus;
        PaymentStatus = OrderPaymentStatus.Cancelled;
        CancelledAt = DateTimeOffset.UtcNow;
        Touch();
    }

    public PaymentRefund ReserveRefund(string idempotencyKey, decimal amount)
    {
        var existing = _refunds.SingleOrDefault(x => x.IdempotencyKey == idempotencyKey);
        if (existing is not null) return existing;
        if (CapturedAmount is null || amount <= 0 || RefundedAmount() + amount > CapturedAmount.Value)
            throw new InvalidOperationException("The refund exceeds the remaining captured amount.");
        var refund = new PaymentRefund(idempotencyKey, amount, Currency);
        _refunds.Add(refund);
        Touch();
        return refund;
    }

    public void RefreshRefundState()
    {
        var completed = _refunds.Where(x => x.Status == PaymentRefundStatus.Completed).Sum(x => x.Amount);
        var hasPending = _refunds.Any(x => x.Status == PaymentRefundStatus.Pending);
        if (hasPending && completed == 0)
            PaymentStatus = OrderPaymentStatus.RefundPending;
        else if (CapturedAmount is not null && completed >= CapturedAmount.Value)
            PaymentStatus = OrderPaymentStatus.Refunded;
        else if (completed > 0 || hasPending)
            PaymentStatus = OrderPaymentStatus.PartiallyRefunded;
        Touch();
    }

    private void Touch()
    {
        PaymentUpdatedAt = DateTimeOffset.UtcNow;
        ConcurrencyToken = Guid.NewGuid().ToString("N");
    }

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
