using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderPayment : BaseEntity, IAggregateRoot
{
    private readonly List<PaymentRefund> _refunds = new();
    private OrderPayment() { }

    public OrderPayment(int orderId, string buyerId, decimal amount, string currency)
    {
        OrderId = orderId;
        BuyerId = buyerId;
        Amount = amount;
        Currency = currency;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public string? PayPalOrderId { get; private set; }
    public string? PayPalOrderStatus { get; private set; }
    public string? AuthorizationId { get; private set; }
    public string? AuthorizationStatus { get; private set; }
    public DateTimeOffset? AuthorizedAt { get; private set; }
    public DateTimeOffset? AuthorizationExpiresAt { get; private set; }
    public string? CaptureId { get; private set; }
    public string? CaptureStatus { get; private set; }
    public decimal? CapturedAmount { get; private set; }
    public decimal? PayPalFee { get; private set; }
    public decimal? NetAmount { get; private set; }
    public DateTimeOffset? CapturedAt { get; private set; }
    public int? PaymentMethodId { get; private set; }
    public string? CardBrand { get; private set; }
    public string? CardLast4 { get; private set; }
    public IReadOnlyCollection<PaymentRefund> Refunds => _refunds.AsReadOnly();
    public decimal RefundedAmount => _refunds.Where(x => x.Status is "COMPLETED" or "PENDING").Sum(x => x.Amount);

    public void RecordPayPalOrder(string id, string status)
    {
        PayPalOrderId = id;
        PayPalOrderStatus = status;
    }

    public void RecordAuthorization(string id, string status, DateTimeOffset? createdAt,
        DateTimeOffset? expiresAt, string? cardBrand, string? cardLast4, int? paymentMethodId,
        string? paypalOrderStatus)
    {
        AuthorizationId = id;
        AuthorizationStatus = status;
        AuthorizedAt = createdAt ?? DateTimeOffset.UtcNow;
        AuthorizationExpiresAt = expiresAt;
        CardBrand = cardBrand;
        CardLast4 = cardLast4;
        PaymentMethodId = paymentMethodId;
        if (!string.IsNullOrWhiteSpace(paypalOrderStatus)) PayPalOrderStatus = paypalOrderStatus;
    }

    public void RefreshAuthorization(string id, string status, DateTimeOffset? createdAt, DateTimeOffset? expiresAt)
    {
        AuthorizationId = id;
        AuthorizationStatus = status;
        AuthorizedAt = createdAt ?? DateTimeOffset.UtcNow;
        AuthorizationExpiresAt = expiresAt;
    }

    public void RecordVoid(string status)
    {
        AuthorizationStatus = status;
    }

    public void RecordCapture(string id, string status, decimal amount, decimal? fee, decimal? net,
        DateTimeOffset? capturedAt)
    {
        CaptureId = id;
        CaptureStatus = status;
        CapturedAmount = amount;
        PayPalFee = fee;
        NetAmount = net;
        CapturedAt = capturedAt ?? DateTimeOffset.UtcNow;
        AuthorizationStatus = "CAPTURED";
    }

    public PaymentRefund AddRefund(string idempotencyKey, string paypalRefundId, string status,
        decimal amount, DateTimeOffset? createdAt)
    {
        var existing = _refunds.SingleOrDefault(x => x.IdempotencyKey == idempotencyKey);
        if (existing is not null) return existing;

        var refund = new PaymentRefund(idempotencyKey, paypalRefundId, status, amount,
            createdAt ?? DateTimeOffset.UtcNow);
        _refunds.Add(refund);
        return refund;
    }

    public void SetCaptureStatus(string status) => CaptureStatus = status;
}

public class PaymentRefund : BaseEntity
{
    private PaymentRefund() { }

    internal PaymentRefund(string idempotencyKey, string paypalRefundId, string status, decimal amount,
        DateTimeOffset createdAt)
    {
        IdempotencyKey = idempotencyKey;
        PayPalRefundId = paypalRefundId;
        Status = status;
        Amount = amount;
        CreatedAt = createdAt;
    }

    public int OrderPaymentId { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string PayPalRefundId { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}
