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
        Status = OrderStatus.AwaitingPayment;
    }

    public string BuyerId { get; private set; }
    public DateTimeOffset OrderDate { get; private set; } = DateTimeOffset.Now;
    public Address ShipToAddress { get; private set; }
    public OrderStatus Status { get; private set; } = OrderStatus.AwaitingPayment;

    public string? PaypalOrderId { get; private set; }
    public string? PaypalAuthorizationId { get; private set; }
    public string? PaypalOriginalAuthorizationId { get; private set; }
    public string? PaypalAuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }

    public string? PaypalCaptureId { get; private set; }
    public string? PaypalCaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PaypalFee { get; private set; }
    public decimal? NetProceeds { get; private set; }
    public string? PaymentCurrency { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }

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
        return decimal.Round(total, 2, MidpointRounding.AwayFromZero);
    }

    public decimal RemainingRefundable()
    {
        if (CapturedAmount is null)
        {
            return 0m;
        }

        var refunded = _refunds.Sum(r => r.Amount);
        var remaining = CapturedAmount.Value - refunded;
        return remaining < 0 ? 0m : decimal.Round(remaining, 2, MidpointRounding.AwayFromZero);
    }

    public OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey)
    {
        return _refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
    }

    public void RecordAuthorization(
        string paypalOrderId,
        string authorizationId,
        string authorizationStatus,
        DateTimeOffset? expiresAt,
        string currency)
    {
        Guard.Against.NullOrEmpty(paypalOrderId, nameof(paypalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));

        PaypalOrderId = paypalOrderId;
        PaypalAuthorizationId = authorizationId;
        PaypalOriginalAuthorizationId ??= authorizationId;
        PaypalAuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        AuthorizedAt = DateTimeOffset.UtcNow;
        PaymentCurrency = currency;
        Status = OrderStatus.Authorized;
    }

    public void RecordReauthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiresAt)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        PaypalAuthorizationId = authorizationId;
        PaypalAuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    public void RecordPaypalOrderId(string paypalOrderId)
    {
        Guard.Against.NullOrEmpty(paypalOrderId, nameof(paypalOrderId));
        PaypalOrderId = paypalOrderId;
    }

    public void RecordCapture(
        string captureId,
        string captureStatus,
        decimal capturedAmount,
        decimal paypalFee,
        decimal netProceeds,
        string currency)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));

        PaypalCaptureId = captureId;
        PaypalCaptureStatus = captureStatus;
        CapturedAmount = decimal.Round(capturedAmount, 2, MidpointRounding.AwayFromZero);
        PaypalFee = decimal.Round(paypalFee, 2, MidpointRounding.AwayFromZero);
        NetProceeds = decimal.Round(netProceeds, 2, MidpointRounding.AwayFromZero);
        PaymentCurrency = currency;
        CapturedAt = DateTimeOffset.UtcNow;
        PaypalAuthorizationStatus = "CAPTURED";
        Status = OrderStatus.Fulfilled;
    }

    public void Cancel()
    {
        if (Status == OrderStatus.Cancelled)
        {
            return;
        }

        if (Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            throw new PaymentConflictException("A fulfilled order cannot be cancelled. Issue a refund instead.");
        }

        Status = OrderStatus.Cancelled;
        CancelledAt = DateTimeOffset.UtcNow;
        if (!string.IsNullOrEmpty(PaypalAuthorizationStatus))
        {
            PaypalAuthorizationStatus = "VOIDED";
        }
    }

    public OrderRefund RecordRefund(string paypalRefundId, string status, decimal amount, string currency, string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(paypalRefundId, nameof(paypalRefundId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var existing = FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        var refund = new OrderRefund(paypalRefundId, status, decimal.Round(amount, 2, MidpointRounding.AwayFromZero), currency, idempotencyKey);
        _refunds.Add(refund);
        Status = RemainingRefundable() <= 0m ? OrderStatus.Refunded : OrderStatus.PartiallyRefunded;
        return refund;
    }
}
