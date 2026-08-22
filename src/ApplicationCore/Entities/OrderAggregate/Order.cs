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
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        BuyerId = buyerId;
        ShipToAddress = shipToAddress;
        _orderItems = items;
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public OrderStatus Status { get; private set; } = OrderStatus.Placed;
    public string? Currency { get; private set; }

    public string? PayPalOrderId { get; private set; }
    public string? PayPalAuthorizationId { get; private set; }
    public string? PayPalAuthorizationStatus { get; private set; }
    public DateTimeOffset? PayPalAuthorizationExpiration { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }

    public string? PayPalCaptureId { get; private set; }
    public string? PayPalCaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    public string? AuthorizeRequestId { get; private set; }
    public string? CaptureRequestId { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }

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

    public bool BelongsTo(string buyerId) =>
        string.Equals(BuyerId, buyerId, StringComparison.Ordinal);

    public void MarkAwaitingPayment(string currency)
    {
        Guard.Against.NullOrEmpty(currency, nameof(currency));
        Status = OrderStatus.AwaitingPayment;
        Currency = currency;
    }

    public void EnsureAuthorizeRequestId()
    {
        AuthorizeRequestId ??= $"eshop-pay-{Id}-{Guid.NewGuid():N}";
    }

    public void EnsureCaptureRequestId()
    {
        CaptureRequestId ??= $"eshop-capture-{Id}-{Guid.NewGuid():N}";
    }

    public void RotateCaptureRequestId()
    {
        CaptureRequestId = $"eshop-capture-{Id}-{Guid.NewGuid():N}";
    }

    public void RecordAuthorization(
        string payPalOrderId,
        string authorizationId,
        string authorizationStatus,
        DateTimeOffset? expiration)
    {
        PayPalOrderId = payPalOrderId;
        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = authorizationStatus;
        PayPalAuthorizationExpiration = expiration;
        AuthorizedAt = DateTimeOffset.UtcNow;
        Status = OrderStatus.Authorized;
    }

    public void ReplaceAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiration)
    {
        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = authorizationStatus;
        PayPalAuthorizationExpiration = expiration;
    }

    public void RecordCapture(
        string captureId,
        string captureStatus,
        decimal capturedAmount,
        decimal paypalFee,
        decimal netAmount)
    {
        PayPalCaptureId = captureId;
        PayPalCaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PayPalFee = paypalFee;
        NetAmount = netAmount;
        CapturedAt = DateTimeOffset.UtcNow;
        PayPalAuthorizationStatus = "CAPTURED";
        Status = OrderStatus.Fulfilled;
    }

    public void RecordCancellation()
    {
        Status = OrderStatus.Cancelled;
        PayPalAuthorizationStatus = string.IsNullOrEmpty(PayPalAuthorizationId) ? PayPalAuthorizationStatus : "VOIDED";
        CancelledAt = DateTimeOffset.UtcNow;
    }

    public OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => string.Equals(r.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));

    public decimal RefundedTotal() => _refunds.Sum(r => r.Amount);

    public decimal RemainingRefundable()
    {
        var captured = CapturedAmount ?? 0m;
        var remaining = captured - RefundedTotal();
        return remaining < 0 ? 0 : remaining;
    }

    public OrderRefund AddRefund(string payPalRefundId, string status, decimal amount, string idempotencyKey)
    {
        var refund = new OrderRefund(payPalRefundId, status, amount, idempotencyKey);
        _refunds.Add(refund);
        RecalculateRefundStatus();
        return refund;
    }

    private void RecalculateRefundStatus()
    {
        var remaining = RemainingRefundable();
        Status = remaining <= 0m ? OrderStatus.Refunded : OrderStatus.PartiallyRefunded;
    }

    public IEnumerable<string> PayPalIdentifiers()
    {
        if (!string.IsNullOrEmpty(PayPalOrderId)) yield return PayPalOrderId;
        if (!string.IsNullOrEmpty(PayPalAuthorizationId)) yield return PayPalAuthorizationId;
        if (!string.IsNullOrEmpty(PayPalCaptureId)) yield return PayPalCaptureId;
        foreach (var refund in _refunds)
        {
            if (!string.IsNullOrEmpty(refund.PayPalRefundId))
            {
                yield return refund.PayPalRefundId;
            }
        }
    }
}
