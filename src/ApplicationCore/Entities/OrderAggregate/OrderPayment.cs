using System;
using System.Collections.Generic;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderPayment : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderPayment() { }
#pragma warning restore CS8618

    public OrderPayment(int orderId, string buyerId, decimal amount, string currency)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NegativeOrZero(amount, nameof(amount));
        Guard.Against.NullOrEmpty(currency, nameof(currency));

        OrderId = orderId;
        BuyerId = buyerId;
        Amount = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        Currency = currency;
        Status = OrderPaymentStatus.AwaitingPayment;
        ReferenceKey = Guid.NewGuid().ToString("N");
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string ReferenceKey { get; private set; }
    public OrderPaymentStatus Status { get; private set; }
    public string Currency { get; private set; }
    public decimal Amount { get; private set; }

    public string? PayPalOrderId { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizationExpiration { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PaypalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public int? SavedPaymentMethodId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private readonly List<OrderRefund> _refunds = new();
    public IReadOnlyCollection<OrderRefund> Refunds => _refunds.AsReadOnly();

    public decimal RefundedAmount => decimal.Round(_refunds.Sum(r => r.Amount), 2, MidpointRounding.AwayFromZero);

    public decimal RemainingRefundable
    {
        get
        {
            var captured = CapturedAmount ?? 0m;
            var remaining = captured - RefundedAmount;
            return remaining < 0 ? 0 : decimal.Round(remaining, 2, MidpointRounding.AwayFromZero);
        }
    }

    public OrderRefund? FindRefundByIdempotencyKey(string idempotencyKey)
    {
        return _refunds.FirstOrDefault(r =>
            string.Equals(r.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));
    }

    public void RecordAuthorization(
        string paypalOrderId,
        string authorizationId,
        string authorizationStatus,
        DateTimeOffset? expiration,
        int? savedPaymentMethodId)
    {
        Guard.Against.NullOrEmpty(paypalOrderId, nameof(paypalOrderId));
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(authorizationStatus, nameof(authorizationStatus));

        PayPalOrderId = paypalOrderId;
        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiration = expiration;
        SavedPaymentMethodId = savedPaymentMethodId;
        Status = OrderPaymentStatus.Authorized;
        Touch();
    }

    public void RefreshAuthorization(string authorizationId, string authorizationStatus, DateTimeOffset? expiration)
    {
        Guard.Against.NullOrEmpty(authorizationId, nameof(authorizationId));
        Guard.Against.NullOrEmpty(authorizationStatus, nameof(authorizationStatus));

        AuthorizationId = authorizationId;
        AuthorizationStatus = authorizationStatus;
        AuthorizationExpiration = expiration;
        Touch();
    }

    public void RecordCapture(string captureId, string captureStatus, decimal capturedAmount, decimal? paypalFee, decimal? netAmount)
    {
        Guard.Against.NullOrEmpty(captureId, nameof(captureId));
        Guard.Against.NullOrEmpty(captureStatus, nameof(captureStatus));
        Guard.Against.NegativeOrZero(capturedAmount, nameof(capturedAmount));

        CaptureId = captureId;
        CaptureStatus = captureStatus;
        CapturedAmount = decimal.Round(capturedAmount, 2, MidpointRounding.AwayFromZero);
        PaypalFee = paypalFee.HasValue ? decimal.Round(paypalFee.Value, 2, MidpointRounding.AwayFromZero) : null;
        NetAmount = netAmount.HasValue ? decimal.Round(netAmount.Value, 2, MidpointRounding.AwayFromZero) : null;
        AuthorizationStatus = "CAPTURED";
        Status = OrderPaymentStatus.Captured;
        Touch();
    }

    public void MarkCancelled(string? authorizationStatus = "VOIDED")
    {
        AuthorizationStatus = authorizationStatus;
        Status = OrderPaymentStatus.Cancelled;
        Touch();
    }

    public OrderRefund AddRefund(string paypalRefundId, string status, decimal amount, string currency, string idempotencyKey)
    {
        var refund = new OrderRefund(paypalRefundId, status, amount, currency, idempotencyKey);
        _refunds.Add(refund);

        var remaining = RemainingRefundable;
        Status = remaining <= 0 ? OrderPaymentStatus.Refunded : OrderPaymentStatus.PartiallyRefunded;
        CaptureStatus = Status == OrderPaymentStatus.Refunded ? "REFUNDED" : "PARTIALLY_REFUNDED";
        Touch();
        return refund;
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
