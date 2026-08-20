using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class Order : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private Order() { }
#pragma warning restore CS8618

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

    private readonly List<OrderItem> _orderItems = new List<OrderItem>();
    public IReadOnlyCollection<OrderItem> OrderItems => _orderItems.AsReadOnly();

    public OrderPaymentStatus Status { get; private set; } = OrderPaymentStatus.AwaitingPayment;
    public string? Currency { get; private set; }
    public string? PaypalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PaypalFee { get; private set; }
    public decimal? NetAmount { get; private set; }

    private readonly List<OrderRefund> _refunds = new();
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

    public OrderRefund? FindRefundByIdempotencyKey(string key) =>
        _refunds.FirstOrDefault(r => r.IdempotencyKey == key);

    public bool IsAuthorizationExpired(DateTimeOffset utcNow, TimeSpan? renewBefore = null)
    {
        if (AuthorizationExpiresAt is null)
        {
            return false;
        }

        var threshold = utcNow + (renewBefore ?? TimeSpan.Zero);
        return AuthorizationExpiresAt <= threshold;
    }

    public void RecordAuthorization(
        string paypalOrderId,
        string authorizationId,
        string? authorizationStatus,
        DateTimeOffset? expiresAt,
        string currency)
    {
        if (Status == OrderPaymentStatus.Authorized
            && AuthorizationId == authorizationId
            && PaypalOrderId == paypalOrderId)
        {
            AuthorizationStatus = authorizationStatus;
            AuthorizationExpiresAt = expiresAt ?? AuthorizationExpiresAt;
            return;
        }

        if (Status != OrderPaymentStatus.AwaitingPayment && Status != OrderPaymentStatus.Authorized)
        {
            throw new InvalidOrderStateException($"Order {Id} cannot be authorized from status {Status}.");
        }

        PaypalOrderId = paypalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
        Currency = currency;
        Status = OrderPaymentStatus.Authorized;
    }

    public void ReplaceAuthorization(string authorizationId, string? authorizationStatus, DateTimeOffset? expiresAt)
    {
        if (Status != OrderPaymentStatus.Authorized)
        {
            throw new InvalidOrderStateException($"Order {Id} has no authorization to renew.");
        }

        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiresAt = expiresAt;
    }

    public void RecordCapture(
        string captureId,
        string? captureStatus,
        decimal capturedAmount,
        decimal? paypalFee,
        decimal? netAmount)
    {
        if (Status == OrderPaymentStatus.Fulfilled && CaptureId == captureId)
        {
            CaptureStatus = captureStatus;
            CapturedAmount = capturedAmount;
            PaypalFee = paypalFee;
            NetAmount = netAmount;
            return;
        }

        if (Status != OrderPaymentStatus.Authorized)
        {
            throw new InvalidOrderStateException($"Order {Id} cannot be fulfilled from status {Status}.");
        }

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = capturedAmount;
        PaypalFee = paypalFee;
        NetAmount = netAmount;
        Status = OrderPaymentStatus.Fulfilled;
    }

    public void Cancel()
    {
        if (Status == OrderPaymentStatus.Cancelled)
        {
            return;
        }

        if (Status is OrderPaymentStatus.Fulfilled or OrderPaymentStatus.Refunded or OrderPaymentStatus.PartiallyRefunded)
        {
            throw new InvalidOrderStateException($"Order {Id} has already been captured and cannot be cancelled. Refund it instead.");
        }

        Status = OrderPaymentStatus.Cancelled;
        AuthorizationStatus = "VOIDED";
    }

    public OrderRefund RecordRefund(string refundId, string idempotencyKey, decimal amount, string? status)
    {
        var existing = FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        if (Status is not (OrderPaymentStatus.Fulfilled or OrderPaymentStatus.PartiallyRefunded))
        {
            throw new InvalidOrderStateException($"Order {Id} cannot be refunded from status {Status}.");
        }

        if (amount <= 0)
        {
            throw new PaymentException("Refund amount must be greater than zero.");
        }

        var remaining = RemainingRefundable();
        if (amount > remaining)
        {
            throw new RefundLimitException(
                $"Refund of {amount:0.00} exceeds the remaining captured amount {remaining:0.00}.");
        }

        var refund = new OrderRefund(refundId, idempotencyKey, amount, status ?? "COMPLETED");
        _refunds.Add(refund);

        Status = RemainingRefundable() == 0m
            ? OrderPaymentStatus.Refunded
            : OrderPaymentStatus.PartiallyRefunded;

        return refund;
    }
}
