using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderPayment : BaseEntity
{
    private readonly List<PaymentRefund> _refunds = new();

    private OrderPayment() { }

    public OrderPayment(string currency, decimal amount)
    {
        if (string.IsNullOrWhiteSpace(currency)) throw new ArgumentException("Currency is required.", nameof(currency));
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
        Currency = currency.ToUpperInvariant();
        Amount = amount;
    }

    public string Currency { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public PaymentStatus Status { get; private set; } = PaymentStatus.AwaitingPayment;
    public string? PayPalOrderId { get; private set; }
    public string? PayPalAuthorizationId { get; private set; }
    public string? PayPalAuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? PayPalCaptureId { get; private set; }
    public string? PayPalCaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetProceeds { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();
    public decimal RefundedAmount => _refunds.Where(x => x.Status is "COMPLETED" or "PENDING").Sum(x => x.Amount);
    public decimal RefundableAmount => Math.Max(0, (CapturedAmount ?? 0) - RefundedAmount);

    public void SetPayPalOrder(string paypalOrderId)
    {
        if (string.IsNullOrWhiteSpace(paypalOrderId)) throw new ArgumentException("PayPal order ID is required.", nameof(paypalOrderId));
        PayPalOrderId = paypalOrderId;
    }

    public void RecordAuthorization(string authorizationId, string status, DateTimeOffset authorizedAt, DateTimeOffset? expiresAt)
    {
        PayPalAuthorizationId = authorizationId;
        PayPalAuthorizationStatus = status;
        AuthorizedAt ??= authorizedAt;
        AuthorizationLastRenewedAt = authorizedAt;
        AuthorizationExpiresAt = expiresAt;
        Status = status == "CREATED" ? PaymentStatus.Authorized : PaymentStatus.AuthorizationPending;
    }

    public DateTimeOffset? AuthorizationLastRenewedAt { get; private set; }

    public void RecordCapture(string captureId, string status, decimal amount, decimal? fee, decimal? net, DateTimeOffset capturedAt)
    {
        PayPalCaptureId = captureId;
        PayPalCaptureStatus = status;
        CapturedAmount = amount;
        PayPalFee = fee;
        NetProceeds = net;
        CapturedAt = capturedAt;
        Status = status == "COMPLETED" ? PaymentStatus.Captured : PaymentStatus.CapturePending;
    }

    public void RecordVoid(string status)
    {
        PayPalAuthorizationStatus = status;
        Status = PaymentStatus.Voided;
    }

    public PaymentRefund AddRefund(string idempotencyKey, string paypalRefundId, string status, decimal amount, DateTimeOffset createdAt)
    {
        var existing = _refunds.SingleOrDefault(x => x.IdempotencyKey == idempotencyKey);
        if (existing is not null) return existing;
        if (amount <= 0 || amount > RefundableAmount) throw new InvalidOperationException("Refund exceeds the remaining captured amount.");

        var remainingBeforeRefund = RefundableAmount;
        var refund = new PaymentRefund(idempotencyKey, paypalRefundId, status, amount, createdAt);
        _refunds.Add(refund);
        Status = amount == remainingBeforeRefund ? PaymentStatus.Refunded : PaymentStatus.PartiallyRefunded;
        PayPalCaptureStatus = Status == PaymentStatus.Refunded ? "REFUNDED" : "PARTIALLY_REFUNDED";
        return refund;
    }
}

public enum PaymentStatus
{
    AwaitingPayment,
    AuthorizationPending,
    Authorized,
    CapturePending,
    Captured,
    PartiallyRefunded,
    Refunded,
    Voided
}

public class PaymentRefund : BaseEntity
{
    private PaymentRefund() { }

    internal PaymentRefund(string idempotencyKey, string paypalRefundId, string status, decimal amount, DateTimeOffset createdAt)
    {
        IdempotencyKey = idempotencyKey;
        PayPalRefundId = paypalRefundId;
        Status = status;
        Amount = amount;
        CreatedAt = createdAt;
    }

    public string IdempotencyKey { get; private set; } = string.Empty;
    public string PayPalRefundId { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
