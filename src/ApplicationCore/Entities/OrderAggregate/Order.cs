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
        Status = OrderStatus.AwaitingPayment;
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;

    public string? PayPalOrderId { get; private set; }
    public string? PayPalCreateRequestId { get; private set; }
    public string? PayPalAuthorizeRequestId { get; private set; }
    public string? PayPalCaptureRequestId { get; private set; }

    public string? PayPalAuthorizationId { get; private set; }
    public string? PayPalOriginalAuthorizationId { get; private set; }
    public string? PayPalAuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiration { get; private set; }
    public DateTimeOffset? OriginalAuthorizedAt { get; private set; }

    public string? PayPalCaptureId { get; private set; }
    public string? PayPalCaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetProceeds { get; private set; }
    public string? PaymentCurrency { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }

    private readonly List<OrderItem> _orderItems = new List<OrderItem>();
    public IReadOnlyCollection<OrderItem> OrderItems => _orderItems.AsReadOnly();

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

    public decimal RefundedTotal() =>
        _refunds.Where(r => r.IsSuccessful).Sum(r => r.Amount);

    public decimal RemainingRefundable() =>
        Math.Max(0, (CapturedAmount ?? 0m) - RefundedTotal());

    public PaymentRefund? FindRefundByIdempotencyKey(string idempotencyKey) =>
        _refunds.FirstOrDefault(r => string.Equals(r.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));

    public void EnsurePaymentRequestIds()
    {
        PayPalCreateRequestId ??= Guid.NewGuid().ToString();
        PayPalAuthorizeRequestId ??= Guid.NewGuid().ToString();
        PayPalCaptureRequestId ??= Guid.NewGuid().ToString();
    }

    public void RecordPayPalOrder(string payPalOrderId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        PayPalOrderId = payPalOrderId;
    }

    public void RecordAuthorization(
        string authorizationId,
        string status,
        DateTimeOffset? expiration,
        DateTimeOffset? createTime,
        string currency)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(status, nameof(status));

        PayPalAuthorizationId = authorizationId;
        PayPalOriginalAuthorizationId ??= authorizationId;
        PayPalAuthorizationStatus = status;
        AuthorizationExpiration = expiration;
        OriginalAuthorizedAt ??= createTime ?? DateTimeOffset.UtcNow;
        PaymentCurrency = currency;
        Status = OrderStatus.PaymentAuthorized;
    }

    public void RecordReauthorization(string authorizationId, string status, DateTimeOffset? expiration)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = status;
        AuthorizationExpiration = expiration;
    }

    public void RecordCapture(
        string captureId,
        string status,
        decimal capturedAmount,
        decimal? paypalFee,
        decimal? netProceeds,
        string currency)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        PayPalCaptureId = captureId;
        PayPalCaptureStatus = status;
        CapturedAmount = capturedAmount;
        PayPalFee = paypalFee;
        NetProceeds = netProceeds;
        PaymentCurrency = currency;
        CapturedAt = DateTimeOffset.UtcNow;
        PayPalAuthorizationStatus = "CAPTURED";
        Status = OrderStatus.Fulfilled;
    }

    public void Cancel()
    {
        Status = OrderStatus.Cancelled;
        PayPalAuthorizationStatus = string.IsNullOrEmpty(PayPalAuthorizationId) ? PayPalAuthorizationStatus : "VOIDED";
    }

    public PaymentRefund AddRefund(string payPalRefundId, string status, decimal amount, string currency, string idempotencyKey)
    {
        var refund = new PaymentRefund(payPalRefundId, status, amount, currency, idempotencyKey);
        _refunds.Add(refund);
        RefreshRefundStatus();
        return refund;
    }

    public void RefreshRefundStatus()
    {
        if (Status is not OrderStatus.Fulfilled and not OrderStatus.PartiallyRefunded and not OrderStatus.Refunded)
        {
            return;
        }

        var remaining = RemainingRefundable();
        Status = remaining <= 0 ? OrderStatus.Refunded : OrderStatus.PartiallyRefunded;
    }

    public bool BelongsTo(string buyerId) =>
        string.Equals(BuyerId, buyerId, StringComparison.Ordinal);
}
