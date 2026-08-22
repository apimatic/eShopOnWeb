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
        Status = OrderPaymentStatus.AwaitingPayment;
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public OrderPaymentStatus Status { get; private set; } = OrderPaymentStatus.AwaitingPayment;

    public string? PayPalOrderId { get; private set; }
    public string? PayPalAuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiration { get; private set; }
    public DateTimeOffset? AuthorizationCreatedAt { get; private set; }
    public string? AuthorizeRequestId { get; private set; }

    public string? PayPalCaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PaypalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public string? PaymentCurrency { get; private set; }
    public string? CaptureRequestId { get; private set; }
    public string? VoidRequestId { get; private set; }

    private readonly List<OrderItem> _orderItems = new List<OrderItem>();
    public IReadOnlyCollection<OrderItem> OrderItems => _orderItems.AsReadOnly();

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

    public decimal RemainingRefundable()
    {
        if (CapturedAmount is null)
        {
            return 0m;
        }

        var refunded = _refunds.Sum(r => r.Amount);
        var remaining = CapturedAmount.Value - refunded;
        return remaining < 0m ? 0m : decimal.Round(remaining, 2, MidpointRounding.AwayFromZero);
    }

    public OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey)
    {
        return _refunds.FirstOrDefault(r =>
            string.Equals(r.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));
    }

    public void SetAuthorizeRequestId(string authorizeRequestId)
    {
        Guard.Against.NullOrEmpty(authorizeRequestId, nameof(authorizeRequestId));
        AuthorizeRequestId ??= authorizeRequestId;
    }

    public void AttachPayPalOrder(string payPalOrderId, string authorizeRequestId)
    {
        Guard.Against.NullOrEmpty(payPalOrderId, nameof(payPalOrderId));
        PayPalOrderId = payPalOrderId;
        AuthorizeRequestId ??= authorizeRequestId;
    }

    public void RecordAuthorization(
        string authorizationId,
        string status,
        DateTimeOffset? expiration,
        DateTimeOffset? createdAt,
        string? currency)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        if (Status is OrderPaymentStatus.Fulfilled or OrderPaymentStatus.Cancelled
            or OrderPaymentStatus.Refunded or OrderPaymentStatus.PartiallyRefunded)
        {
            throw new InvalidOperationException($"Cannot authorize an order in status {Status}.");
        }

        PayPalAuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiration = expiration;
        AuthorizationCreatedAt = createdAt;
        PaymentCurrency = currency ?? PaymentCurrency;
        Status = OrderPaymentStatus.Authorized;
    }

    public void RecordReauthorization(
        string authorizationId,
        string status,
        DateTimeOffset? expiration,
        DateTimeOffset? createdAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        PayPalAuthorizationId = authorizationId;
        AuthorizationStatus = status;
        AuthorizationExpiration = expiration;
        AuthorizationCreatedAt = createdAt;
    }

    public void RecordCapture(
        string captureId,
        string status,
        decimal capturedAmount,
        decimal? paypalFee,
        decimal? netAmount,
        string currency,
        string captureRequestId)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        PayPalCaptureId = captureId;
        CaptureStatus = status;
        CapturedAmount = capturedAmount;
        PaypalFee = paypalFee;
        NetAmount = netAmount;
        PaymentCurrency = currency;
        CaptureRequestId = captureRequestId;
        AuthorizationStatus = "CAPTURED";
        Status = OrderPaymentStatus.Fulfilled;
    }

    public void MarkCancelled(string? voidRequestId)
    {
        if (Status is OrderPaymentStatus.Fulfilled or OrderPaymentStatus.Refunded
            or OrderPaymentStatus.PartiallyRefunded)
        {
            throw new InvalidOperationException("A fulfilled order cannot be cancelled; refund it instead.");
        }

        VoidRequestId = voidRequestId ?? VoidRequestId;
        AuthorizationStatus = Status == OrderPaymentStatus.Authorized ? "VOIDED" : AuthorizationStatus;
        Status = OrderPaymentStatus.Cancelled;
    }

    public OrderRefund AddRefund(string payPalRefundId, decimal amount, string currency, string status, string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(payPalRefundId, nameof(payPalRefundId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var refund = new OrderRefund(payPalRefundId, amount, currency, status, idempotencyKey);
        _refunds.Add(refund);

        var remaining = RemainingRefundable();
        Status = remaining <= 0m ? OrderPaymentStatus.Refunded : OrderPaymentStatus.PartiallyRefunded;
        if (remaining <= 0m)
        {
            CaptureStatus = "REFUNDED";
        }
        else
        {
            CaptureStatus = "PARTIALLY_REFUNDED";
        }

        return refund;
    }
}
